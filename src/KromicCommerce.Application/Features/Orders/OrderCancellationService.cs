using System.Text.Json;
using KromicCommerce.Application.Abstractions.Payments;
using KromicCommerce.Application.Services;

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
///
/// Known failure mode — refund succeeded, database commit failed
/// -------------------------------------------------------------
/// The Razorpay refund is an external call and cannot participate in the PostgreSQL
/// transaction. If the refund is accepted but the final SaveChangesAsync then fails, the
/// database still shows an uncancelled order whose payment is recorded as un-refunded, while
/// the customer has in fact been made whole at the provider.
///
/// That window is deliberately left to a recovery process rather than papered over. What makes
/// it SAFE and recoverable, rather than a silent double-refund hazard, is that step 1 commits
/// the refund record on its own before touching the order:
///   - A retry finds <c>payment.IsRefunded == true</c>, skips the provider call entirely, and
///     proceeds straight to cancellation — so the order reaches its correct terminal state
///     without paying the customer twice.
///   - The idempotency key <c>order-cancel:{orderId}</c> means that even a retry that did reach
///     the provider would be de-duplicated by Razorpay.
/// So the correct recovery for an order stuck after a successful refund is simply to retry the
/// cancellation. Automated reconciliation of a half-committed refund is deliberately NOT
/// attempted here: attempting an automatic second refund against a payment that may already
/// have been returned would risk paying twice, and that is strictly worse than a manual review
/// queue.
/// </summary>
internal sealed class OrderCancellationService(
    IApplicationDbContext db,
    IPaymentGateway paymentGateway,
    OrderInventoryRestorer inventoryRestorer,
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
        // 2. Return stock to sellable.
        //
        // Deliberately BEFORE the order is transitioned. If this fails we return without
        // having mutated the order at all, so the failure leaves a genuinely clean state
        // rather than one that merely happens not to have been saved. Cancelling anyway
        // would strand the units permanently, because the state machine forbids
        // re-cancelling a Cancelled order, so the restore could never be retried.
        //
        // A refund recorded in step 1 is already committed at this point. A retry therefore
        // skips the provider call and comes straight back here, which is the intended
        // idempotent recovery.
        //
        // Staging does NOT save. The restore, the order transition and the outbox event are
        // all committed by the single SaveChangesAsync in step 3, so EF wraps them in one
        // transaction and stock can never be restored against an order that stayed active.
        // -----------------------------------------------------------------------
        IReadOnlyList<Guid> restoredProductIds;
        try
        {
            restoredProductIds = await inventoryRestorer.StageRestoreAsync(order, ct);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(Error.Conflict("INVENTORY_RESTORE_FAILED", ex.Message));
        }

        // -----------------------------------------------------------------------
        // 3. Only now is the order cancelled. The transition is legal (checked in step 0),
        //    the money has been returned, and the stock is back.
        // -----------------------------------------------------------------------
        try
        {
            order.Cancel(reason);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(Error.Conflict("INVALID_ORDER_TRANSITION", ex.Message));
        }

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

        // Only after the commit succeeded — invalidating earlier could repopulate the cache
        // from a transaction that then rolled back.
        await inventoryRestorer.InvalidateCachesAsync(restoredProductIds, ct);

        logger.LogInformation(
            "Order {OrderId} cancelled by {CancelledBy}. Reason: {Reason}",
            order.Id, cancelledBy, reason ?? "none");

        return Result.Success();
    }
}
