using System.Text.Json;
using KromicCommerce.Application.Abstractions.Payments;
using KromicCommerce.Application.Features.Orders.GetMyOrders;
using KromicCommerce.Contracts.Orders;

namespace KromicCommerce.Application.Features.Orders.CancelOrder;

/// <summary>
/// Handles customer self-cancellation.
///
/// Cancellation is only allowed while the order is in PendingPayment or Confirmed state.
/// Once the store starts processing/packing, the customer can no longer cancel — they must
/// contact support.
///
/// Refund rules (same as admin cancel):
///   - COD or unpaid Razorpay → no refund needed.
///   - Paid Razorpay → refund is initiated automatically. On success the order moves to
///     RefundPending. On failure the cancellation still stands but the user is told to
///     contact support.
/// </summary>
internal sealed class CancelOrderHandler(
    IApplicationDbContext db,
    IPaymentGateway paymentGateway,
    ILogger<CancelOrderHandler> logger)
    : ICommandHandler<CancelOrderCommand, OrderResponse>
{
    // Statuses from which a customer is allowed to cancel
    private static readonly HashSet<OrderStatus> CancellableStatuses =
        [OrderStatus.PendingPayment, OrderStatus.Confirmed];

    public async Task<Result<OrderResponse>> Handle(CancelOrderCommand command, CancellationToken ct)
    {
        var order = await db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(
                o => o.Id == command.OrderId && o.CustomerId == command.CustomerId, ct);

        if (order is null)
            return Result.Failure<OrderResponse>(Error.NotFound("ORDER_NOT_FOUND", "Order not found."));

        // Enforce pre-processing restriction
        if (!CancellableStatuses.Contains(order.Status))
            return Result.Failure<OrderResponse>(Error.Conflict("CANNOT_CANCEL",
                "This order can no longer be cancelled. Please contact support if you need assistance."));

        try { order.Cancel(command.Reason); }
        catch (InvalidOperationException ex)
        {
            return Result.Failure<OrderResponse>(Error.Conflict("INVALID_ORDER_TRANSITION", ex.Message));
        }

        // Release inventory reservations
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
                    "Inventory release failed during customer cancel. " +
                    "OrderId: {OrderId} ProductId: {ProductId} Qty: {Qty}",
                    command.OrderId, item.ProductId, item.Quantity);
            }
        }

        // Conditional refund for paid Razorpay orders
        if (order.PaymentMethod == PaymentMethod.Razorpay)
        {
            var payment = await db.Payments.FirstOrDefaultAsync(
                p => p.OrderId == order.Id && p.Status == PaymentStatus.Paid, ct);

            if (payment is not null && !string.IsNullOrWhiteSpace(payment.ProviderPaymentId))
            {
                var note = string.IsNullOrWhiteSpace(command.Reason)
                    ? "Cancelled by customer"
                    : $"Cancelled by customer: {command.Reason}";

                var refundResult = await paymentGateway.RefundAsync(
                    payment.ProviderPaymentId, payment.Amount, note, ct);

                if (refundResult.Success)
                {
                    try { order.MarkRefundPending(); }
                    catch (InvalidOperationException)
                    {
                        logger.LogWarning("Could not mark order {OrderId} RefundPending after customer refund", order.Id);
                    }
                    logger.LogInformation("Refund initiated for customer-cancelled Order {OrderId}. RefundId: {RefundId}",
                        order.Id, refundResult.ProviderRefundId);
                }
                else
                {
                    logger.LogError("Customer refund failed for Order {OrderId}: {Error}",
                        order.Id, refundResult.ErrorMessage);
                    // Cancellation still proceeds — refund failure must not block the cancel
                }
            }
        }

        var payload = JsonSerializer.Serialize(new
        {
            order.Id, order.OrderNumber, order.CustomerId, order.CurrencyCode
        });
        db.OutboxEvents.Add(OutboxEvent.Create("OrderCancelled", payload));

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Order {OrderId} cancelled by customer. Reason: {Reason}",
            command.OrderId, command.Reason ?? "none");

        var imageMap = await GetMyOrderByIdHandler.LoadImageMapAsync(db, order.Items, ct);
        return Result.Success(GetMyOrderByIdHandler.MapToResponse(order, imageMap));
    }
}
