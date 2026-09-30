using System.Text.Json;
using KromicCommerce.Application.Services;
using KromicCommerce.Domain.Promotions;

namespace KromicCommerce.Application.Features.Checkout;

/// <summary>
/// Verifies the payment signature returned by the Razorpay widget.
/// The signature is validated server-side — the frontend cannot fake a successful payment.
///
/// On success:
///   - Marks payment as paid.
///   - Transitions order PendingPayment → OrderPlaced (paid, awaiting merchant confirmation).
///   - Does NOT confirm the order — admin must do that after verifying stock.
///   - Does NOT finalize inventory — inventory finalizes when admin confirms.
///   - Records promotion usage.
///   - Publishes PaymentSucceeded outbox event → sends payment confirmation email.
///
/// On failure:
///   - Marks payment and order as failed, releases inventory.
/// </summary>
internal sealed class VerifyPaymentHandler(
    IApplicationDbContext db,
    IPaymentGateway paymentGateway,
    OrderInventoryRestorer inventoryRestorer,
    ILogger<VerifyPaymentHandler> logger)
    : ICommandHandler<VerifyPaymentCommand, PaymentResponse>
{
    public async Task<Result<PaymentResponse>> Handle(
        VerifyPaymentCommand command, CancellationToken cancellationToken)
    {
        var order = await db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(
                o => o.Id == command.OrderId && o.CustomerId == command.CustomerId,
                cancellationToken);

        if (order is null)
            return Result.Failure<PaymentResponse>(
                Error.NotFound("ORDER_NOT_FOUND", "Order not found."));

        var payment = await db.Payments
            .FirstOrDefaultAsync(p => p.OrderId == command.OrderId, cancellationToken);

        if (payment is null)
            return Result.Failure<PaymentResponse>(
                Error.NotFound("PAYMENT_NOT_FOUND", "Payment record not found."));

        if (payment.Status == PaymentStatus.Paid)
        {
            // Idempotent — already verified
            return Result.Success(MapPayment(payment));
        }

        // Verify signature — this is the only authoritative confirmation
        var signatureValid = await paymentGateway.VerifyPaymentSignatureAsync(
            command.RazorpayOrderId,
            command.RazorpayPaymentId,
            command.RazorpaySignature,
            cancellationToken);

        if (!signatureValid)
        {
            logger.LogWarning(
                "Payment signature verification FAILED. OrderId: {OrderId} PaymentId: {PaymentId}",
                command.OrderId, command.RazorpayPaymentId);

            payment.MarkFailed("Signature verification failed.");
            order.MarkFailed();

            // Return the reserved units. The order never reaches Confirmed, so the units are
            // still in the Reserved bucket and never left OnHand.
            //
            // Staged only — not saved. The release, the failed payment and the failed order all
            // commit in the single SaveChangesAsync below, so a failure can never leave units
            // released against an order that is not actually Failed (or vice versa).
            var restoredProductIds = await StageInventoryReleaseAsync(order, cancellationToken);

            await db.SaveChangesAsync(cancellationToken);

            // Only after the commit — invalidating first could repopulate the cache from a
            // transaction that then rolled back.
            await inventoryRestorer.InvalidateCachesAsync(restoredProductIds, cancellationToken);

            return Result.Failure<PaymentResponse>(
                Error.Unauthorized("PAYMENT_VERIFICATION_FAILED",
                    "Payment signature verification failed."));
        }

        // -----------------------------------------------------------------------
        // Payment successful.
        // Order transitions PendingPayment → OrderPlaced (paid, awaiting merchant confirmation).
        // Admin must still confirm the order after verifying stock availability.
        // Inventory is NOT finalized here — it finalizes when admin confirms.
        // -----------------------------------------------------------------------
        payment.MarkPaid(command.RazorpayPaymentId);
        order.MarkPaymentReceived(); // PendingPayment → OrderPlaced, sets PaidAt

        // Record promotion usage now that payment is confirmed
        if (!string.IsNullOrWhiteSpace(order.AppliedCouponCode))
        {
            var promotion = await db.Promotions
                .FirstOrDefaultAsync(p => p.CouponCode == order.AppliedCouponCode, cancellationToken);
            if (promotion is not null && promotion.HasRemainingUsage())
            {
                promotion.IncrementUsage();
                db.PromotionUsages.Add(PromotionUsage.Create(promotion.Id, order.CustomerId, order.Id));
            }
        }

        // PaymentSucceeded event → sends payment confirmation email to customer
        var outboxPayload = JsonSerializer.Serialize(new
        {
            order.Id, order.OrderNumber, order.CustomerId,
            order.GrandTotal, order.CurrencyCode
        });
        db.OutboxEvents.Add(OutboxEvent.Create("PaymentSucceeded", outboxPayload));

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Payment verified. OrderId: {OrderId} ProviderPaymentId: {PaymentId} — awaiting merchant confirmation.",
            command.OrderId, command.RazorpayPaymentId);

        return Result.Success(MapPayment(payment));
    }

    /// <summary>
    /// Returns reserved units when a payment attempt fails verification.
    ///
    /// A failed order never reached Confirmed, so its units are still Reserved and never left
    /// OnHand. The restore is driven by each line's persisted inventory status rather than by
    /// current counters, so a duplicate or concurrent failure callback cannot release units it
    /// did not reserve. See <see cref="OrderInventoryRestorer"/> for the idempotency and
    /// concurrency guarantees.
    ///
    /// Failures are surfaced rather than swallowed: a silently skipped release would strand
    /// units in Reserved forever, and the order is already Failed so nothing would ever attempt
    /// the release again.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> StageInventoryReleaseAsync(Order order, CancellationToken ct)
    {
        try
        {
            return await inventoryRestorer.StageRestoreAsync(order, ct);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException(
                $"Inventory release failed for failed order {order.OrderNumber}. {ex.Message}",
                ex);
        }
    }

    private static PaymentResponse MapPayment(Payment p) =>
        new(p.Id, p.OrderId, p.Provider, p.ProviderPaymentId,
            p.Amount, p.CurrencyCode, p.Status, p.PaidAt, p.CreatedAtUtc);
}
