namespace KromicCommerce.Application.Features.Orders.Admin;

internal sealed class UpdateOrderStatusHandler(
    IApplicationDbContext db,
    ILogger<UpdateOrderStatusHandler> logger)
    : ICommandHandler<UpdateOrderStatusCommand>
{
    public async Task<Result> Handle(UpdateOrderStatusCommand command, CancellationToken ct)
    {
        var order = await db.Orders.FindAsync([command.OrderId], ct);
        if (order is null)
            return Result.Failure(Error.NotFound("ORDER_NOT_FOUND", "Order not found."));

        if (!Enum.TryParse<OrderStatus>(command.Status, out var newStatus))
            return Result.Failure(Error.Validation("INVALID_STATUS", $"Unknown order status: {command.Status}."));

        try
        {
            switch (newStatus)
            {
                case OrderStatus.Processing:  order.MarkProcessing(); break;
                case OrderStatus.Packed:      order.MarkPacked(); break;
                case OrderStatus.Shipped:
                    order.MarkShipped(command.TrackingNumber, command.TrackingProvider); break;
                case OrderStatus.Delivered:   order.MarkDelivered(); break;
                case OrderStatus.Cancelled:   order.Cancel(command.Reason); break;
                case OrderStatus.RefundPending: order.MarkRefundPending(); break;
                case OrderStatus.Refunded:    order.MarkRefunded(); break;
                default:
                    return Result.Failure(Error.Validation("INVALID_TRANSITION",
                        $"Cannot manually transition to {newStatus}."));
            }
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(Error.Conflict("INVALID_ORDER_TRANSITION", ex.Message));
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Order {OrderId} transitioned to {Status}", command.OrderId, newStatus);
        return Result.Success();
    }
}
