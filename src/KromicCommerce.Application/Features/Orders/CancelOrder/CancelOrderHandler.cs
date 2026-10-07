using KromicCommerce.Application.Features.Catalog.Products.Variants;
using KromicCommerce.Application.Features.Orders.GetMyOrders;
using KromicCommerce.Contracts.Catalog;
using KromicCommerce.Contracts.Orders;

namespace KromicCommerce.Application.Features.Orders.CancelOrder;

/// <summary>
/// Handles customer self-cancellation.
///
/// Cancellation is only allowed while the order is in PendingPayment or Confirmed state.
/// Once the store starts processing/packing, the customer can no longer cancel — they must
/// contact support.
///
/// The refund-then-cancel ordering, idempotency and error contract live in
/// <see cref="OrderCancellationService"/> and are shared verbatim with the admin cancel flow.
/// In short: a captured Razorpay payment is refunded first, and if the provider rejects the
/// refund this handler writes nothing at all and returns REFUND_FAILED.
internal sealed class CancelOrderHandler(
    IApplicationDbContext db,
    OrderCancellationService cancellation)
    : ICommandHandler<CancelOrderCommand, OrderResponse>
{
    // Customer self-cancellation is deliberately stricter than the admin cancel flow, which
    // uses Order.CanCancel. Once the store has started processing or packing, the customer must
    // contact support rather than self-serve. OrderCancellationService re-checks CanCancel
    // before it moves any money, so this set is a UX restriction, not the safety guarantee.
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

        var result = await cancellation.CancelAsync(order, command.Reason, "customer", ct);

        if (!result.IsSuccess)
        {
            // The order is unchanged — report exactly why so the customer does not believe
            // the order was cancelled.
            return Result.Failure<OrderResponse>(result.Error);
        }

        var imageMap = await GetMyOrderByIdHandler.LoadImageMapAsync(db, order.Items, ct);
        
        // Resolve variant attributes for order items that have variants
        var variantAttributeMap = await GetMyOrderByIdHandler.BuildVariantAttributeMapAsync(db, order.Items, ct);
        
        return Result.Success(GetMyOrderByIdHandler.MapToResponse(order, imageMap, variantAttributeMap));
    }
}
