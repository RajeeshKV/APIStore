using System.Text.Json;
using KromicCommerce.Application.Features.Orders.GetMyOrders;
using KromicCommerce.Contracts.Orders;

namespace KromicCommerce.Application.Features.Orders.Admin;

internal sealed class UpdateOrderStatusHandler(
    IApplicationDbContext db,
    OrderCancellationService cancellation,
    ILogger<UpdateOrderStatusHandler> logger)
    : ICommandHandler<UpdateOrderStatusCommand, OrderResponse>
{
    public async Task<Result<OrderResponse>> Handle(UpdateOrderStatusCommand command, CancellationToken ct)
    {
        var order = await db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == command.OrderId, ct);

        if (order is null)
            return Result.Failure<OrderResponse>(Error.NotFound("ORDER_NOT_FOUND", "Order not found."));

        // -----------------------------------------------------------------------
        // Admin cancel — refunds a captured Razorpay payment before cancelling
        // -----------------------------------------------------------------------
        if (command.Status == OrderStatus.Cancelled)
        {
            var cancelled = await cancellation.CancelAsync(order, command.Reason, "admin", ct);
            if (!cancelled.IsSuccess)
                return Result.Failure<OrderResponse>(cancelled.Error);

            return Result.Success(await BuildResponseAsync(order, ct));
        }

        // -----------------------------------------------------------------------
        // Standard status transitions
        // -----------------------------------------------------------------------
        try
        {
            switch (command.Status)
            {
                case OrderStatus.Confirmed:
                    order.Confirm();
                    // When admin confirms, finalize inventory (move reserved → fulfilled)
                    // This applies to both COD (reserved at checkout) and Razorpay (reserved at checkout)
                    await FinalizeInventoryAsync(order, ct);
                    break;
                case OrderStatus.Processing:    order.MarkProcessing(); break;
                case OrderStatus.Packed:        order.MarkPacked(); break;
                case OrderStatus.Shipped:
                    order.MarkShipped(command.TrackingNumber, command.TrackingProvider); break;
                case OrderStatus.Delivered:     order.MarkDelivered(); break;
                case OrderStatus.RefundPending: order.MarkRefundPending(); break;
                case OrderStatus.Refunded:      order.MarkRefunded(); break;
                default:
                    return Result.Failure<OrderResponse>(Error.Validation("INVALID_TRANSITION",
                        $"Cannot manually transition to {command.Status}."));
            }
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure<OrderResponse>(Error.Conflict("INVALID_ORDER_TRANSITION", ex.Message));
        }

        // Publish outbox event for every status transition — drives automated customer emails
        db.OutboxEvents.Add(OutboxEvent.Create(
            GetEventType(command.Status),
            BuildPayload(order, command)));

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Order {OrderId} transitioned to {Status} by admin",
            command.OrderId, command.Status);

        return Result.Success(await BuildResponseAsync(order, ct));
    }

    // -----------------------------------------------------------------------
    // Inventory helpers
    // -----------------------------------------------------------------------

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

    // -----------------------------------------------------------------------
    // Outbox event helpers — every admin transition generates a customer email
    // -----------------------------------------------------------------------

    private static string GetEventType(OrderStatus status) => status switch
    {
        OrderStatus.Confirmed     => "OrderConfirmed",
        OrderStatus.Processing    => "OrderProcessing",
        OrderStatus.Packed        => "OrderPacked",
        OrderStatus.Shipped       => "OrderShipped",
        OrderStatus.Delivered     => "OrderDelivered",
        OrderStatus.RefundPending => "OrderRefundPending",
        OrderStatus.Refunded      => "OrderRefunded",
        _                         => $"OrderStatus_{status}"
    };

    private static string BuildPayload(Order order, UpdateOrderStatusCommand cmd) =>
        JsonSerializer.Serialize(new
        {
            order.Id, order.OrderNumber, order.CustomerId,
            order.GrandTotal, order.CurrencyCode,
            TrackingNumber = cmd.TrackingNumber,
            TrackingProvider = cmd.TrackingProvider
        });

    private async Task<OrderResponse> BuildResponseAsync(Order order, CancellationToken ct)
    {
        var imageMap = await GetMyOrderByIdHandler.LoadImageMapAsync(db, order.Items, ct);
        return GetMyOrderByIdHandler.MapToResponse(order, imageMap);
    }
}
