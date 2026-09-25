using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage;

namespace KromicCommerce.Application.Features.Checkout;

/// <summary>
/// Processes a payment provider webhook atomically and idempotently.
///
/// Correctness guarantees:
///   1. Signature verified before any DB work.
///   2. The duplicate check, business state update, and WebhookEvent insert all happen
///      inside a single database transaction. There is exactly one SaveChangesAsync call.
///   3. The unique index on (Provider, ProviderEventId) is the authoritative idempotency
///      gate. A concurrent duplicate webhook will hit a DbUpdateException (unique violation)
///      which is caught and mapped to Result.Success() — Razorpay retries are satisfied.
///   4. Never trusts client-supplied payment status — only verified webhook body.
///   5. Inventory helpers log failures rather than silently swallowing them.
/// </summary>
internal sealed class HandlePaymentWebhookHandler(
    IApplicationDbContext db,
    IPaymentGateway paymentGateway,
    ILogger<HandlePaymentWebhookHandler> logger)
    : ICommandHandler<HandlePaymentWebhookCommand>
{
    public async Task<Result> Handle(
        HandlePaymentWebhookCommand command, CancellationToken cancellationToken)
    {
        // Step 1 — Verify signature (rejects tampered/invalid webhooks)
        var verified = paymentGateway.VerifyWebhook(command.RawPayload, command.Signature);
        if (verified is null)
        {
            logger.LogWarning("Webhook signature verification failed. Provider: {Provider}", command.Provider);
            return Result.Failure(Error.Unauthorized("WEBHOOK_INVALID_SIGNATURE",
                "Webhook signature is invalid."));
        }

        // Step 2 — Optimistic early duplicate check (avoids unnecessary DB work)
        if (verified.ProviderEventId is not null)
        {
            var alreadyProcessed = await db.WebhookEvents.AnyAsync(
                e => e.ProviderEventId == verified.ProviderEventId && e.Provider == command.Provider,
                cancellationToken);

            if (alreadyProcessed)
            {
                logger.LogInformation(
                    "Duplicate webhook ignored (pre-check). Provider: {Provider} EventId: {EventId}",
                    command.Provider, verified.ProviderEventId);
                return Result.Success();
            }
        }

        // Step 3 — All mutations in a single transaction with one SaveChangesAsync
        // The unique index on WebhookEvents(Provider, ProviderEventId) is the definitive
        // idempotency guard. A concurrent duplicate that passes the AnyAsync check above
        // will be caught by a DbUpdateException (unique constraint) from the transaction.
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

            var payment = await db.Payments
                .FirstOrDefaultAsync(p => p.ProviderOrderId == verified.ProviderOrderId, cancellationToken);

            if (payment is null)
            {
                logger.LogWarning(
                    "Webhook received but no matching payment found. ProviderOrderId: {Id}",
                    verified.ProviderOrderId);
                // Persist to prevent endless retries for unknown events; still atomic
                AddWebhookEvent(command, verified);
                await db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
                return Result.Success();
            }

            var order = await db.Orders
                .FirstOrDefaultAsync(o => o.Id == payment.OrderId, cancellationToken);

            if (order is null)
            {
                AddWebhookEvent(command, verified);
                await db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
                return Result.Success();
            }

            // Apply business state changes
            if (verified.IsPaymentSucceeded && payment.Status != PaymentStatus.Paid)
            {
                payment.MarkPaid(verified.ProviderPaymentId ?? "webhook");
                if (order.Status == OrderStatus.PendingPayment ||
                    order.Status == OrderStatus.PaymentProcessing)
                {
                    order.Confirm();
                    await FinalizeInventoryAsync(order, cancellationToken);
                }

                var payload = JsonSerializer.Serialize(new
                {
                    order.Id, order.OrderNumber,
                    order.CustomerId, order.GrandTotal, order.CurrencyCode
                });
                db.OutboxEvents.Add(OutboxEvent.Create("PaymentSucceeded", payload));
            }
            else if (verified.IsPaymentFailed && payment.Status != PaymentStatus.Failed)
            {
                payment.MarkFailed(verified.FailureReason);
                if (order.Status is OrderStatus.PendingPayment or OrderStatus.PaymentProcessing)
                {
                    order.MarkFailed();
                    await ReleaseInventoryAsync(order, cancellationToken);
                }
            }

            // Add WebhookEvent — unique index prevents duplicate commits
            AddWebhookEvent(command, verified);

            // Single SaveChangesAsync: business state + webhook event atomically
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            logger.LogInformation(
                "Webhook processed. Provider: {Provider} EventType: {Type} OrderId: {OrderId}",
                command.Provider, verified.EventType, order.Id);

            return Result.Success();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Concurrent duplicate webhook hit the unique index — treat as already processed
            logger.LogInformation(
                "Duplicate webhook caught by unique constraint. Provider: {Provider} EventId: {EventId}",
                command.Provider, verified.ProviderEventId);
            return Result.Success();
        }
    }

    private void AddWebhookEvent(HandlePaymentWebhookCommand cmd, WebhookVerificationResult verified)
    {
        var we = WebhookEvent.Create(
            cmd.Provider,
            verified.ProviderEventId ?? Guid.NewGuid().ToString(),
            verified.EventType,
            cmd.RawPayload);
        we.MarkProcessed();
        db.WebhookEvents.Add(we);
    }

    private async Task FinalizeInventoryAsync(Order order, CancellationToken ct)
    {
        var productIds = order.Items.Select(i => i.ProductId).ToList();
        var inventoryItems = await db.InventoryItems
            .Where(i => productIds.Contains(i.ProductId))
            .ToListAsync(ct);

        foreach (var item in order.Items)
        {
            var inv = inventoryItems.FirstOrDefault(
                i => i.ProductId == item.ProductId && i.VariantId == item.VariantId);
            if (inv is null) continue;
            try { inv.FinalizeReservation(item.Quantity); }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "FinalizeReservation failed for OrderItem {ItemId} Product {ProductId}",
                    item.Id, item.ProductId);
            }
        }
    }

    private async Task ReleaseInventoryAsync(Order order, CancellationToken ct)
    {
        var productIds = order.Items.Select(i => i.ProductId).ToList();
        var inventoryItems = await db.InventoryItems
            .Where(i => productIds.Contains(i.ProductId))
            .ToListAsync(ct);

        foreach (var item in order.Items)
        {
            var inv = inventoryItems.FirstOrDefault(
                i => i.ProductId == item.ProductId && i.VariantId == item.VariantId);
            if (inv is null) continue;
            try { inv.Release(item.Quantity); }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Release failed for OrderItem {ItemId} Product {ProductId}",
                    item.Id, item.ProductId);
            }
        }
    }

    /// <summary>
    /// Detects PostgreSQL unique-constraint violation (SQLSTATE 23505) from Npgsql.
    /// Allows the caller to map a duplicate-key DB error to an idempotent success response.
    /// </summary>
    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException?.Message.Contains("23505", StringComparison.Ordinal) == true ||
        ex.InnerException?.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) == true;
}
