using System.Text.Json;
using KromicCommerce.Application.Features.Orders.GetMyOrders;
using KromicCommerce.Application.Services;
using KromicCommerce.Contracts.Orders;

namespace KromicCommerce.Application.Features.Orders.Admin;

internal sealed class UpdateOrderStatusHandler(
    IApplicationDbContext db,
    OrderCancellationService cancellation,
    OrderInventoryRestorer inventoryRestorer,
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
        // Products whose stock actually moved, so the availability caches can be evicted after the
        // commit. Declared outside the try because the cache step runs after it.
        IReadOnlyList<Guid> consumedProductIds = [];
        try
        {
            switch (command.Status)
            {
                case OrderStatus.Confirmed:
                    // Consume stock BEFORE transitioning the order. Finalization is the
                    // fallible step, so it must run first: if it throws, the order has not been
                    // mutated at all and confirmation can be retried once the shortfall is
                    // resolved. Doing it in the other order would leave the in-memory order
                    // Confirmed with only some of its stock consumed.
                    consumedProductIds = await FinalizeInventoryAsync(order, ct);
                    order.Confirm();
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

        // Stock actually moved (units left Reserved and OnHand), so the cached availability
        // projections are stale. Only after the commit — invalidating first could repopulate
        // the cache from a transaction that then rolled back.
        await inventoryRestorer.InvalidateCachesAsync(consumedProductIds, ct);

        logger.LogInformation("Order {OrderId} transitioned to {Status} by admin",
            command.OrderId, command.Status);

        return Result.Success(await BuildResponseAsync(order, ct));
    }

    // -----------------------------------------------------------------------
    // Inventory helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Moves reserved units to sold. Applies identically to COD and Razorpay: both reserve at
    /// checkout and finalise at this transition, so the two payment methods share one
    /// consumption point.
    ///
    /// Failures are NOT swallowed. If any line cannot be finalised the whole confirmation is
    /// abandoned and nothing is saved, leaving the order at OrderPlaced with its reservation
    /// intact. That matters because a partially-finalised order cannot be repaired afterwards:
    /// the order is already Confirmed, so re-confirming throws and the shortfall is permanent.
    /// The customer has paid at this point, so the recovery path is to retry confirmation or
    /// cancel (which refunds and releases) — both existing operations.
    ///
    /// Each line's persisted inventory status is moved Reserved -&gt; Finalized, recording
    /// that these specific units were sold. That record is what a later cancellation reads to
    /// decide it must add exactly <c>InventoryQuantity</c> back to OnHand, instead of guessing
    /// from the current counters.
    ///
    /// Staging only — the single SaveChangesAsync in Handle commits the finalization, the order
    /// transition and the outbox event together, so a multi-line order can never end up with
    /// some lines finalized while the order stayed OrderPlaced.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> FinalizeInventoryAsync(Order order, CancellationToken ct)
    {
        var trackable = order.Items.Where(i => i.CanRestoreInventory).ToList();
        if (trackable.Count == 0) return [];

        var productIds = trackable.Select(i => i.ProductId).ToList();
        var inventoryItems = await db.InventoryItems
            .Where(i => productIds.Contains(i.ProductId))
            .ToListAsync(ct);

// Pair each line with the inventory row that owns its stock, skipping products that have
        // no row at all — those are not stock-tracked, which is legitimate for products created
        // before inventory tracking existed and must not block confirmation.
        var targets = trackable
            .Select(item => new
            {
                Item = item,
                Inventory = inventoryItems.FirstOrDefault(
                    i => i.ProductId == item.ProductId && i.VariantId == item.VariantId)
            })
            .Where(t => t.Inventory is not null)
            .Select(t => new { t.Item, Inventory = t.Inventory! })
            .ToList();

        // A line that has already been finalised cannot be consumed again. The order state
        // machine normally prevents re-confirmation, but a line already moved to Finalized
        // would otherwise be double-consumed and permanently over-deduct from OnHand.
        var notReserved = trackable
            .Where(i => i.InventoryStatus != OrderItemInventoryStatus.Reserved)
            .ToList();
        if (notReserved.Count > 0)
            throw new InvalidOperationException(
                $"Cannot confirm order {order.OrderNumber}: line {notReserved[0].Id} is " +
                $"{notReserved[0].InventoryStatus}, not Reserved. The order was left " +
                "unconfirmed; its inventory was already consumed or returned.");

        // Validate every line BEFORE consuming any. Consuming line by line would leave a
        // half-applied change if a later line fell short, and those mutated entities would
        // remain dirty in the change tracker.
        foreach (var target in targets)
        {
            // Finalize exactly the quantity recorded for this line, which is what a later
            // cancellation will hand back.
            if (!target.Inventory.CanFinalizeReservation(target.Item.InventoryQuantity))
            {
                throw new InvalidOperationException(
                    $"Cannot confirm order {order.OrderNumber}: only " +
                    $"{target.Inventory.Reserved} units are reserved for product " +
                    $"{target.Item.ProductId} but {target.Item.InventoryQuantity} are required. " +
                    "The order was left unconfirmed; resolve the stock shortfall and retry.");
            }
        }

        // Every line is satisfiable, so this cannot partially fail.
        foreach (var target in targets)
        {
            target.Inventory.FinalizeReservation(target.Item.InventoryQuantity);
            target.Item.MarkInventoryFinalized();
        }

        return targets.Select(t => t.Item.ProductId).Distinct().ToList();
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
