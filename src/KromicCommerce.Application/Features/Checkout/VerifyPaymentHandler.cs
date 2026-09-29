using System.Text.Json;
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
        var signatureValid = paymentGateway.VerifyPaymentSignature(
            command.RazorpayOrderId,
            command.RazorpayPaymentId,
            command.RazorpaySignature);

        if (!signatureValid)
        {
            logger.LogWarning(
                "Payment signature verification FAILED. OrderId: {OrderId} PaymentId: {PaymentId}",
                command.OrderId, command.RazorpayPaymentId);

            payment.MarkFailed("Signature verification failed.");
            order.MarkFailed();

            // Release inventory reservations on payment failure
            await ReleaseInventoryAsync(order, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

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
                    "Release failed during payment failure for OrderItem {ItemId} Product {ProductId}",
                    item.Id, item.ProductId);
            }
        }
    }

    private static PaymentResponse MapPayment(Payment p) =>
        new(p.Id, p.OrderId, p.Provider, p.ProviderPaymentId,
            p.Amount, p.CurrencyCode, p.Status, p.PaidAt, p.CreatedAtUtc);
}
