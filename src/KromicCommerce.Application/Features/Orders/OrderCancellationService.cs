using System.Text.Json;
using KromicCommerce.Application.Abstractions.Payments;

namespace KromicCommerce.Application.Features.Orders;

/// <summary>
/// Single implementation of order cancellation, shared by the customer self-cancel and the
/// admin cancel flows so the two can never diverge.
///
/// Ordering contract (this is the whole point of this class)
/// -----------------------------------------------------------------------
/// For a captured online payment, the refund is issued and PERSISTED FIRST, and only then is
/// the order transitioned to Cancelled. Consequences:
///
///   - Refund rejected by the provider → no order state change at all. The order keeps its
///     original status, inventory stays reserved and the caller receives a meaningful error.
///     The order is never marked Cancelled against a payment that was not returned.
///   - Refund accepted → the payment's refund record is committed on its own before the
///     order is cancelled. That commit is the idempotency checkpoint: a retried cancellation
///     sees a refunded payment and skips the provider call, so a crash or an impatient retry
///     cannot produce a second refund against the same captured payment.
///
/// A payment that was never captured (pending, failed) needs no refund, and a cash-on-delivery
/// order never has a captured online payment, so both proceed straight to cancellation.
/// </summary>
internal sealed class OrderCancellationService(
    IApplicationDbContext db,
    IPaymentGateway paymentGateway,
    ILogger<OrderCancellationService> logger)
{
    /// <summary>
    /// Cancels <paramref name="order"/>, refunding first when a captured online payment exists.
    /// On success the order, its inventory reservations and the outbox notification are
    /// persisted. On failure nothing is written and the error describes why.
    /// </summary>
    /// <param name="cancelledBy">"customer" or "admin" — used for the provider note only.</param>
    public async Task<Result> CancelAsync(
        Order order, string? reason, string cancelledBy, CancellationToken ct)
    {
        // -----------------------------------------------------------------------
        // 0. Pre-flight. Refunding is irreversible, so the transition must already be legal
        //    before any money moves.
        // -----------------------------------------------------------------------
        if (!order.CanCancel)
            return Result.Failure(Error.Conflict(
                "INVALID_ORDER_TRANSITION",
                $"Order cannot transition from {order.Status} to {OrderStatus.Cancelled}."));

        // -----------------------------------------------------------------------
        // 1. Refund a captured online payment, and persist the result before cancelling.
        // -----------------------------------------------------------------------
        if (order.PaymentMethod == PaymentMethod.Razorpay)
        {
            var payment = await db.Payments
                .FirstOrDefaultAsync(p => p.OrderId == order.Id, ct);

            // Only a captured payment with a provider id can be refunded. Anything else
            // (pending, failed, already refunded, or no provider id) needs no gateway call.
            var needsRefund =
                payment is not null
                && payment.Status == PaymentStatus.Paid
                && !string.IsNullOrWhiteSpace(payment.ProviderPaymentId);

            if (needsRefund)
            {
                var note = string.IsNullOrWhiteSpace(reason)
                    ? $"Cancelled by {cancelledBy}"
                    : $"Cancelled by {cancelledBy}: {reason}";

                // Stable across retries for the same order — the provider de-duplicates on it.
                var refundResult = await paymentGateway.RefundAsync(
                    payment!.ProviderPaymentId!,
                    payment.Amount,
                    note,
                    idempotencyKey: $"order-cancel:{order.Id:N}",
                    cancellationToken: ct);

                if (!refundResult.Success)
                {
                    // The order is left exactly as it was. No cancellation, no inventory
                    // release, no customer notification — because nothing was cancelled.
                    logger.LogError(
                        "Refund failed for {CancelledBy}-cancelled Order {OrderId}: {Error}. " +
                        "Order status left unchanged at {Status}.",
                        cancelledBy, order.Id, refundResult.ErrorMessage, order.Status);

                    return Result.Failure(Error.Conflict(
                        "REFUND_FAILED",
                        $"The order could not be cancelled because the refund was not accepted " +
                        $"by the payment provider: {refundResult.ErrorMessage} " +
                        "The order is unchanged. Please try again, or process the refund manually " +
                        "and cancel the order afterwards."));
                }

                payment.MarkRefunded(refundResult.ProviderRefundId, payment.Amount);

                // Committed on its own before the cancellation. If the cancellation step below
                // fails, the recorded refund is still the reason a retry will not refund twice.
                await db.SaveChangesAsync(ct);

                logger.LogInformation(
                    "Refund accepted for {CancelledBy}-cancelled Order {OrderId}. RefundId: {RefundId} Amount: {Amount}",
                    cancelledBy, order.Id, refundResult.ProviderRefundId, payment.Amount);
            }
            else if (payment is { IsRefunded: true })
            {
                logger.LogInformation(
                    "Order {OrderId} already has a recorded refund (Status {PaymentStatus}); " +
                    "skipping the provider call and completing cancellation.",
                    order.Id, payment.Status);
            }
        }

        // -----------------------------------------------------------------------
        // 2. Only now is the order cancelled. The transition is legal (checked in step 0)
        //    and the money has already been returned.
        // -----------------------------------------------------------------------
        try
        {
            order.Cancel(reason);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(Error.Conflict("INVALID_ORDER_TRANSITION", ex.Message));
        }

        // -----------------------------------------------------------------------
        // 3. Release reserved stock and notify.
        // -----------------------------------------------------------------------
        await ReleaseInventoryAsync(order, ct);

        db.OutboxEvents.Add(OutboxEvent.Create(
            "OrderCancelled",
            JsonSerializer.Serialize(new
            {
                order.Id,
                order.OrderNumber,
                order.CustomerId,
                order.GrandTotal,
                order.CurrencyCode,
                Reason = reason
            })));

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Order {OrderId} cancelled by {CancelledBy}. Reason: {Reason}",
            order.Id, cancelledBy, reason ?? "none");

        return Result.Success();
    }

    /// <summary>
    /// Releases reserved inventory for every order line. Failures are logged but do not
    /// abort the cancellation: the order is already cancelled and the provider has the money,
    /// so refusing to persist would leave the database disagreeing with reality.
    /// </summary>
    private async Task ReleaseInventoryAsync(Order order, CancellationToken ct)
    {
        var productIds = order.Items.Select(i => i.ProductId).ToList();
        if (productIds.Count == 0) return;

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
                    "Inventory release failed during cancellation. OrderId: {OrderId} " +
                    "OrderItemId: {ItemId} ProductId: {ProductId} Qty: {Qty}",
                    order.Id, item.Id, item.ProductId, item.Quantity);
            }
        }
    }
}
