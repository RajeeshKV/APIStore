using System.Text.Json;
using KromicCommerce.Application.Abstractions.Email;
using KromicCommerce.Application.Abstractions.Store;
using KromicCommerce.Infrastructure.Configuration;
using KromicCommerce.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KromicCommerce.Infrastructure.BackgroundServices;

/// <summary>
/// Background service that polls the OutboxEvents table and dispatches pending events.
/// Runs on a configurable interval (BackgroundWorkerOptions.OutboxIntervalSeconds).
/// Retries up to MaxRetries before permanently failing an event.
/// Email/SMS failures do NOT corrupt the core order state — side effects are decoupled.
/// Never logs credentials, tokens, or sensitive payload contents.
/// </summary>
internal sealed class OutboxProcessor(
    IServiceScopeFactory scopeFactory,
    IOptions<BackgroundWorkerOptions> workerOptions,
    ILogger<OutboxProcessor> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("OutboxProcessor started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            await ProcessBatchAsync(stoppingToken);

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(workerOptions.Value.OutboxIntervalSeconds),
                    stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Host is shutting down — exit the loop cleanly without logging as an error
                break;
            }
        }

        logger.LogInformation("OutboxProcessor stopped.");
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var emailSvc = scope.ServiceProvider.GetRequiredService<IEmailService>();
        var businessSettings = scope.ServiceProvider.GetRequiredService<IBusinessSettingsService>();

        var opts = workerOptions.Value;

        var pending = await db.OutboxEvents
            .Where(e =>
                e.ProcessedAt == null &&
                e.RetryCount < opts.OutboxMaxRetries)
            .OrderBy(e => e.CreatedAt)
            .Take(opts.OutboxBatchSize)
            .ToListAsync(ct);

        if (!pending.Any()) return;

        var settings = await businessSettings.GetAsync(ct);

        foreach (var evt in pending)
        {
            try
            {
                await DispatchAsync(evt, db, emailSvc, settings, ct);
                evt.MarkProcessed();
            }
            catch (Exception ex)
            {
                evt.RecordFailure(ex.Message);
                logger.LogWarning(ex,
                    "OutboxEvent dispatch failed. EventType: {Type} RetryCount: {Retry}",
                    evt.EventType, evt.RetryCount);

                // Mark permanently failed when max retries exceeded so it's
                // excluded from future queries AND visible in operational monitoring
                if (evt.HasFailed(opts.OutboxMaxRetries))
                {
                    evt.MarkProcessed(); // seal it so it stops retrying
                    logger.LogError(
                        "OutboxEvent permanently failed after {MaxRetries} retries. " +
                        "EventType: {Type} EventId: {Id}",
                        opts.OutboxMaxRetries, evt.EventType, evt.Id);
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task DispatchAsync(
        OutboxEvent evt,
        AppDbContext db,
        IEmailService emailSvc,
        BusinessSettings? settings,
        CancellationToken ct)
    {
        switch (evt.EventType)
        {
            case "OrderCreated":
            case "PaymentSucceeded":
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(evt.Payload);
                if (data is null) return;

                var orderId = data["Id"].GetGuid();
                var customerId = data.ContainsKey("CustomerId")
                    ? data["CustomerId"].GetGuid()
                    : Guid.Empty;

                var user = await db.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == customerId, ct);

                if (user is null) return;

                var grandTotal = data.ContainsKey("GrandTotal")
                    ? data["GrandTotal"].GetDecimal() : 0m;
                var currency = data.ContainsKey("CurrencyCode")
                    ? data["CurrencyCode"].GetString() ?? "INR" : "INR";
                var orderNumber = data.ContainsKey("OrderNumber")
                    ? data["OrderNumber"].GetString() ?? "" : "";

                var ctx = new OrderEmailContext(
                    user.Email, user.FullName,
                    orderNumber, grandTotal, currency,
                    settings?.BusinessName ?? "Store",
                    settings?.LogoUrl, settings?.SupportEmail, settings?.WebsiteUrl);

                if (evt.EventType == "OrderCreated")
                    await emailSvc.SendOrderConfirmationAsync(ctx, ct);
                else
                    await emailSvc.SendPaymentConfirmationAsync(ctx, ct);

                break;
            }
            case "OrderCancelled":
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(evt.Payload);
                if (data is null) return;

                var customerId = data.ContainsKey("CustomerId")
                    ? data["CustomerId"].GetGuid() : Guid.Empty;
                var orderNumber = data.ContainsKey("OrderNumber")
                    ? data["OrderNumber"].GetString() ?? "" : "";
                var currency = data.ContainsKey("CurrencyCode")
                    ? data["CurrencyCode"].GetString() ?? "INR" : "INR";

                var user = await db.Users.AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == customerId, ct);
                if (user is null) return;

                var ctx = new OrderEmailContext(
                    user.Email, user.FullName, orderNumber, 0m, currency,
                    settings?.BusinessName ?? "Store",
                    settings?.LogoUrl, settings?.SupportEmail, settings?.WebsiteUrl);

                await emailSvc.SendOrderCancelledAsync(ctx, null, ct);
                break;
            }

            default:
                logger.LogDebug("Unhandled outbox event type: {Type}", evt.EventType);
                evt.MarkProcessed(); // mark done so it doesn't retry indefinitely
                break;
        }
    }
}
