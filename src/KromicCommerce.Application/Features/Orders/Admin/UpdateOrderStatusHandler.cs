using KromicCommerce.Application.Abstractions.Payments;

namespace KromicCommerce.Application.Features.Orders.Admin;

internal sealed class UpdateOrderStatusHandler(
    IApplicationDbContext db,
    IPaymentGateway paymentGateway,
    ILogger<UpdateOrderStatusHandler> logger)
    : ICommandHandler<UpdateOrderStatusCommand>
{
    public async Task<Result> Handle(UpdateOrderStatusCommand command, CancellationToken ct)
    {
        var order = await db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == command.OrderId, ct);

        if (order is null)
            return Result.Failure(Error.NotFound("ORDER_NOT_FOUND", "Order not found."));

        // -----------------------------------------------------------------------
        // Admin cancel — may trigger refund for paid Razorpay orders
        // -----------------------------------------------------------------------
        if (command.Status == OrderStatus.Cancelled)
            return await CancelOrderAsync(order, command.Reason, ct);

        // -----------------------------------------------------------------------
        // Standard status transitions
        // -----------------------------------------------------------------------
        try
        {
            switch (command.Status)
            {
                case OrderStatus.Confirmed:       order.Confirm(); break;
                case OrderStatus.Processing:      order.MarkProcessing(); break;
                case OrderStatus.Packed:          order.MarkPacked(); break;
                case OrderStatus.Shipped:
                    order.MarkShipped(command.TrackingNumber, command.TrackingProvider); break;
                case OrderStatus.Delivered:       order.MarkDelivered(); break;
                case OrderStatus.RefundPending:   order.MarkRefundPending(); break;
                case OrderStatus.Refunded:        order.MarkRefunded(); break;
                default:
                    return Result.Failure(Error.Validation("INVALID_TRANSITION",
                        $"Cannot manually transition to {command.Status}."));
            }
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(Error.Conflict("INVALID_ORDER_TRANSITION", ex.Message));
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Order {OrderId} transitioned to {Status} by admin",
            command.OrderId, command.Status);
        return Result.Success();
    }

    // -----------------------------------------------------------------------
    // Cancel + conditional refund
    //
    // Rules:
    //   - COD or unpaid Razorpay → cancel only, no refund.
    //   - Paid Razorpay → initiate refund via gateway, mark order RefundPending.
    //     The order transitions to Refunded when the Razorpay webhook confirms it,
    //     or the admin can manually move it to Refunded.
    //   - Refund failure is non-fatal: order is still cancelled but admin is warned
    //     via the response error so they can retry manually.
    // -----------------------------------------------------------------------
    private async Task<Result> CancelOrderAsync(Order order, string? reason, CancellationToken ct)
    {
        try { order.Cancel(reason); }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(Error.Conflict("INVALID_ORDER_TRANSITION", ex.Message));
        }

        // Check for a paid Razorpay payment requiring refund
        if (order.PaymentMethod == PaymentMethod.Razorpay)
        {
            var payment = await db.Payments.FirstOrDefaultAsync(
                p => p.OrderId == order.Id && p.Status == PaymentStatus.Paid, ct);

            if (payment is not null && !string.IsNullOrWhiteSpace(payment.ProviderPaymentId))
            {
                var refundNote = string.IsNullOrWhiteSpace(reason)
                    ? "Cancelled by admin"
                    : $"Cancelled by admin: {reason}";

                var refundResult = await paymentGateway.RefundAsync(
                    payment.ProviderPaymentId,
                    payment.Amount,
                    refundNote,
                    ct);

                if (refundResult.Success)
                {
                    // Move order to RefundPending — transitions to Refunded via webhook or manual admin action
                    try { order.MarkRefundPending(); }
                    catch (InvalidOperationException)
                    {
                        // Already in a terminal state — just log and continue
                        logger.LogWarning(
                            "Could not mark order {OrderId} as RefundPending after refund initiation",
                            order.Id);
                    }

                    logger.LogInformation(
                        "Refund initiated for cancelled Order {OrderId}. RefundId: {RefundId}",
                        order.Id, refundResult.ProviderRefundId);
                }
                else
                {
                    // Cancellation still stands — refund failed, admin must retry via Razorpay dashboard
                    logger.LogError(
                        "Refund failed for cancelled Order {OrderId}. Error: {Error}. " +
                        "Admin must initiate refund manually via Razorpay dashboard.",
                        order.Id, refundResult.ErrorMessage);

                    await db.SaveChangesAsync(ct);
                    return Result.Failure(Error.Conflict("REFUND_FAILED",
                        $"Order cancelled but refund could not be initiated: {refundResult.ErrorMessage}. " +
                        "Please process the refund manually via the Razorpay dashboard."));
                }
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Order {OrderId} cancelled by admin. Reason: {Reason}",
            order.Id, reason ?? "none");
        return Result.Success();
    }
}
