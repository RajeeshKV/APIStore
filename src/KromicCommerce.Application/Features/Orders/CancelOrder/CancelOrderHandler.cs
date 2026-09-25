using System.Text.Json;

namespace KromicCommerce.Application.Features.Orders.CancelOrder;

internal sealed class CancelOrderHandler(
    IApplicationDbContext db,
    ILogger<CancelOrderHandler> logger)
    : ICommandHandler<CancelOrderCommand>
{
    public async Task<Result> Handle(CancelOrderCommand command, CancellationToken ct)
    {
        var order = await db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(
                o => o.Id == command.OrderId && o.CustomerId == command.CustomerId, ct);

        if (order is null)
            return Result.Failure(Error.NotFound("ORDER_NOT_FOUND", "Order not found."));

        try
        {
            order.Cancel(command.Reason);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(Error.Conflict("INVALID_ORDER_TRANSITION", ex.Message));
        }

        // Batch-load all inventory items for this order in one query (avoids N+1)
        var productIds = order.Items.Select(i => i.ProductId).ToList();
        var inventoryItems = await db.InventoryItems
            .Where(i => productIds.Contains(i.ProductId))
            .ToListAsync(ct);

        foreach (var item in order.Items)
        {
            var inv = inventoryItems.FirstOrDefault(
                i => i.ProductId == item.ProductId && i.VariantId == item.VariantId);
            if (inv is null) continue;

            try
            {
                inv.Release(item.Quantity);
            }
            catch (Exception ex)
            {
                // Log but do not abort cancellation — inventory inconsistency must not
                // prevent the order from being cancelled. Ops team can reconcile via logs.
                logger.LogError(ex,
                    "Inventory release failed during order cancellation. " +
                    "OrderId: {OrderId} ProductId: {ProductId} Quantity: {Qty}",
                    command.OrderId, item.ProductId, item.Quantity);
            }
        }

        var payload = JsonSerializer.Serialize(new
        {
            order.Id, order.OrderNumber, order.CustomerId, order.CurrencyCode
        });
        db.OutboxEvents.Add(OutboxEvent.Create("OrderCancelled", payload));

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Order {OrderId} cancelled. CustomerId: {CustomerId} Reason: {Reason}",
            command.OrderId, command.CustomerId, command.Reason);
        return Result.Success();
    }
}
