using System.Text.Json;
using KromicCommerce.Application.Abstractions.Email;
using KromicCommerce.Application.Abstractions.Store;
using KromicCommerce.Application.Options;
using KromicCommerce.Infrastructure.Configuration;
using KromicCommerce.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KromicCommerce.Infrastructure.BackgroundServices;

/// <summary>
/// Background service that polls the OutboxEvents table and dispatches pending events.
/// Runs on BackgroundWorkerOptions.OutboxIntervalSeconds.
/// Retries up to MaxRetries before permanently sealing a failed event.
/// Email/SMS failures do NOT corrupt the core order state — side effects are decoupled.
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
        var appOptions = scope.ServiceProvider.GetRequiredService<IOptions<AppPublicOptions>>().Value;

        var opts = workerOptions.Value;

        var pending = await db.OutboxEvents
            .Where(e => e.ProcessedAt == null && e.RetryCount < opts.OutboxMaxRetries)
            .OrderBy(e => e.CreatedAt)
            .Take(opts.OutboxBatchSize)
            .ToListAsync(ct);

        if (!pending.Any()) return;

        var settings = await businessSettings.GetAsync(ct);

        foreach (var evt in pending)
        {
            try
            {
                await DispatchAsync(evt, db, emailSvc, settings, appOptions, ct);
                evt.MarkProcessed();
            }
            catch (Exception ex)
            {
                evt.RecordFailure(ex.Message);
                logger.LogWarning(ex,
                    "OutboxEvent dispatch failed. EventType: {Type} RetryCount: {Retry}",
                    evt.EventType, evt.RetryCount);

                if (evt.HasFailed(opts.OutboxMaxRetries))
                {
                    evt.MarkProcessed();
                    logger.LogError(
                        "OutboxEvent permanently failed after {MaxRetries} retries. EventType: {Type} Id: {Id}",
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
        AppPublicOptions appOptions,
        CancellationToken ct)
    {
        switch (evt.EventType)
        {
            // -----------------------------------------------------------------------
            // Order placed — customer submitted a new order
            // -----------------------------------------------------------------------
            case "OrderPlaced":
            {
                var ctx = await BuildOrderContextAsync(evt, db, settings, appOptions, ct);
                if (ctx is null) return;
                await emailSvc.SendOrderPlacedAsync(ctx, ct);
                break;
            }

            // -----------------------------------------------------------------------
            // Legacy OrderCreated — maps to OrderPlaced email
            // -----------------------------------------------------------------------
            case "OrderCreated":
            {
                var ctx = await BuildOrderContextAsync(evt, db, settings, appOptions, ct);
                if (ctx is null) return;
                await emailSvc.SendOrderPlacedAsync(ctx, ct);
                break;
            }

            // -----------------------------------------------------------------------
            // Merchant confirmed the order
            // -----------------------------------------------------------------------
            case "OrderConfirmed":
            {
                var ctx = await BuildOrderContextAsync(evt, db, settings, appOptions, ct);
                if (ctx is null) return;
                await emailSvc.SendOrderConfirmedAsync(ctx, ct);
                break;
            }

            // -----------------------------------------------------------------------
            // Order processing / packed — informational, no dedicated template yet
            // -----------------------------------------------------------------------
            case "OrderProcessing":
            case "OrderPacked":
            {
                // No dedicated email for these statuses — mark processed silently
                logger.LogDebug("No email template for event type {Type}. Skipping.", evt.EventType);
                break;
            }

            // -----------------------------------------------------------------------
            // Order shipped
            // -----------------------------------------------------------------------
            case "OrderShipped":
            {
                var raw = ParsePayload(evt);
                if (raw is null) return;
                var data = raw.Value;

                var ctx = await BuildOrderContextAsync(evt, db, settings, appOptions, ct);
                if (ctx is null) return;

                var tracking = data.TryGetProperty("TrackingNumber", out var t) ? t.GetString() : null;
                var provider = data.TryGetProperty("TrackingProvider", out var p) ? p.GetString() : null;
                await emailSvc.SendOrderShippedAsync(ctx, tracking, provider, ct);
                break;
            }

            // -----------------------------------------------------------------------
            // Order delivered
            // -----------------------------------------------------------------------
            case "OrderDelivered":
            {
                var ctx = await BuildOrderContextAsync(evt, db, settings, appOptions, ct);
                if (ctx is null) return;
                await emailSvc.SendOrderDeliveredAsync(ctx, ct);
                break;
            }

            // -----------------------------------------------------------------------
            // Order cancelled
            // -----------------------------------------------------------------------
            case "OrderCancelled":
            {
                var raw = ParsePayload(evt);
                if (raw is null) return;
                var data = raw.Value;

                var ctx = await BuildOrderContextAsync(evt, db, settings, appOptions, ct);
                if (ctx is null) return;

                var reason = data.TryGetProperty("Reason", out var r) ? r.GetString() : null;
                await emailSvc.SendOrderCancelledAsync(ctx, reason, ct);
                break;
            }

            // -----------------------------------------------------------------------
            // Refund
            // -----------------------------------------------------------------------
            case "OrderRefundPending":
            {
                // No dedicated email — refund confirmed email sent on OrderRefunded
                logger.LogDebug("OrderRefundPending received — no email sent.");
                break;
            }

            case "OrderRefunded":
            {
                var ctx = await BuildOrderContextAsync(evt, db, settings, appOptions, ct);
                if (ctx is null) return;
                await emailSvc.SendOrderRefundedAsync(ctx, ct);
                break;
            }

            // -----------------------------------------------------------------------
            // Razorpay payment captured
            // -----------------------------------------------------------------------
            case "PaymentSucceeded":
            {
                var ctx = await BuildOrderContextAsync(evt, db, settings, appOptions, ct);
                if (ctx is null) return;
                await emailSvc.SendPaymentConfirmationAsync(ctx, ct);
                break;
            }

            // -----------------------------------------------------------------------
            // Non-email integration events — mark processed, no retry needed
            // -----------------------------------------------------------------------
            case "RazorpayConfigurationUpdated":
            case "GoogleOAuthConfigurationUpdated":
            case "IntegrationConfigUpdated":
            {
                logger.LogDebug("Audit-only outbox event {Type} — no action needed.", evt.EventType);
                break;
            }

            default:
                logger.LogDebug("Unhandled outbox event type: {Type}", evt.EventType);
                break;
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static JsonElement? ParsePayload(OutboxEvent evt)
    {
        try
        {
            using var doc = JsonDocument.Parse(evt.Payload);
            return doc.RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }

    private async Task<OrderEmailContext?> BuildOrderContextAsync(
        OutboxEvent evt,
        AppDbContext db,
        BusinessSettings? settings,
        AppPublicOptions appOptions,
        CancellationToken ct)
    {
        var data = ParsePayload(evt);
        if (data is null) return null;

        if (!data.Value.TryGetProperty("CustomerId", out var cidEl)) return null;
        var customerId = cidEl.GetGuid();

        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == customerId, ct);
        if (user is null) return null;

        var orderNumber = data.Value.TryGetProperty("OrderNumber", out var on)
            ? on.GetString() ?? string.Empty : string.Empty;
        var grandTotal = data.Value.TryGetProperty("GrandTotal", out var gt)
            ? gt.GetDecimal() : 0m;
        var currency = data.Value.TryGetProperty("CurrencyCode", out var cc)
            ? cc.GetString() ?? "INR" : "INR";

        return new OrderEmailContext(
            CustomerEmail: user.Email,
            CustomerName: user.FullName,
            OrderNumber: orderNumber,
            GrandTotal: grandTotal,
            Currency: currency,
            BusinessName: settings?.BusinessName ?? "Store",
            LogoUrl: settings?.LogoUrl,
            SupportEmail: settings?.SupportEmail,
            WebsiteUrl: settings?.WebsiteUrl,
            FrontendUrl: appOptions.FrontendUrl);
    }
}
