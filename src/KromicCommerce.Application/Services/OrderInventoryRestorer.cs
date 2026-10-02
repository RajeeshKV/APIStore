using KromicCommerce.Application.Abstractions.Catalog;
using KromicCommerce.Domain.Catalog;
using KromicCommerce.Domain.Orders;

namespace KromicCommerce.Application.Services;

/// <summary>
/// Returns an order's units to sellable stock, driven by the PERSISTED per-line inventory
/// lifecycle rather than by whatever the inventory counters happen to say right now.
///
/// Shared by cancellation and the payment-failure release so the two can never diverge.
///
/// Why persisted state and not current counters
/// --------------------------------------------
/// The obvious implementation — "take units out of Reserved if there are any, otherwise add to
/// OnHand" — is unsound. By the time an order is cancelled, <c>Reserved</c> may hold units held
/// for a completely different order, and <c>OnHand</c> may have been topped up by a restock.
/// Inferring the historical split from those numbers can return somebody else's reservation and
/// permanently strand this order's own units. <see cref="OrderItem.InventoryStatus"/> records
/// what actually happened to this specific line, so there is no ambiguity.
///
/// Idempotency
/// -----------
/// Two independent guards, because either alone is insufficient:
///
///   1. In-memory: a line already <c>Restored</c> is skipped, and
///      <see cref="OrderItem.MarkInventoryRestored"/> throws if asked twice. This handles a
///      sequential retry.
///   2. Database: <see cref="OrderItem.Version"/> is an xmin concurrency token. Two genuinely
///      simultaneous cancellations both load the line as Finalized and both try to write
///      Restored; PostgreSQL accepts only the first UPDATE, so the loser gets
///      <c>DbUpdateConcurrencyException</c> and its whole SaveChanges rolls back — including its
///      inventory changes. This is what makes it safe across multiple API instances, which a
///      lock in a single process could not be.
///
/// Atomicity
/// ---------
/// This type deliberately performs NO SaveChanges. It stages changes in the change tracker and
/// lets the caller commit them together with the order state transition, so "stock restored" and
/// "order cancelled" land in one transaction and can never be half-applied.
/// </summary>
internal sealed class OrderInventoryRestorer(
    IApplicationDbContext db,
    ICatalogCacheService cache,
    ILogger<OrderInventoryRestorer> logger)
{
    /// <summary>
    /// Stages the restore of every line that still holds units, without saving.
    ///
    /// Validates all lines before mutating any, so a failure cannot leave dirty half-applied
    /// entities in the change tracker.
    /// </summary>
    /// <returns>The product ids whose stock changed, for cache invalidation after commit.</returns>
    /// <exception cref="InvalidOperationException">A line cannot be restored as recorded.</exception>
    public async Task<IReadOnlyList<Guid>> StageRestoreAsync(Order order, CancellationToken ct)
    {
        // Already-restored lines are skipped rather than treated as an error: a repeated
        // cancellation of the same order is a legitimate no-op, not a fault.
        var restorable = order.Items.Where(i => i.CanRestoreInventory).ToList();
        if (restorable.Count == 0) return [];

        var productIds = restorable.Select(i => i.ProductId).Distinct().ToList();

        var inventoryItems = await db.InventoryItems
            .Where(i => productIds.Contains(i.ProductId))
            .ToListAsync(ct);

        var byKey = new Dictionary<(Guid ProductId, Guid? VariantId), InventoryItem>();
        foreach (var inv in inventoryItems)
            byKey[(inv.ProductId, inv.VariantId)] = inv;

        var targets = new List<(OrderItem Item, InventoryItem Inventory, OrderItemInventoryStatus From)>();

        foreach (var item in restorable)
        {
            // A tracked line whose inventory row has since been deleted cannot be restored.
            // Surfacing this is safer than silently skipping, which would strand the units.
            if (!byKey.TryGetValue((item.ProductId, item.VariantId), out var inv))
                throw new InvalidOperationException(
                    $"Order {order.OrderNumber}: order line {item.Id} is recorded as " +
                    $"{item.InventoryStatus} but its inventory row for product " +
                    $"{item.ProductId} (variant {item.VariantId?.ToString() ?? "none"}) no longer " +
                    "exists. The units cannot be returned automatically. The order was not " +
                    "changed. Restore the stock row and retry.");

            var qty = item.InventoryQuantity;

            if (item.InventoryStatus == OrderItemInventoryStatus.Finalized)
            {
                if (!inv.CanRestoreSoldUnits(qty))
                    throw new InvalidOperationException(
                        $"Order {order.OrderNumber}: restoring {qty} sold units for product " +
                        $"{item.ProductId} would overflow on-hand stock.");
            }
            else // Reserved
            {
                // Units are still held for this order; they only leave the Reserved bucket.
                // They were never deducted from OnHand, so putting them back would inflate stock.
                if (inv.Reserved < qty)
                    throw new InvalidOperationException(
                        $"Order {order.OrderNumber}: {qty} units are recorded as reserved for " +
                        $"product {item.ProductId} but only {inv.Reserved} are currently " +
                        "reserved. Another operation may have released them. The order was not " +
                        "changed. Reconcile the reservation and retry.");
            }

            targets.Add((item, inv, item.InventoryStatus));
        }

        foreach (var (item, inv, from) in targets)
        {
            if (from == OrderItemInventoryStatus.Finalized)
                inv.RestoreSoldUnits(item.InventoryQuantity);
            else
                inv.Release(item.InventoryQuantity);

            item.MarkInventoryRestored();
        }

        logger.LogInformation(
            "Staged inventory restore for Order {OrderNumber}: {LineCount} line(s) returned to sellable stock.",
            order.OrderNumber, targets.Count);

        return targets.Select(t => t.Item.ProductId).Distinct().ToList();
    }

    /// <summary>
    /// Evicts the cached projections that embed stock for the given products.
    ///
    /// Must be called only AFTER a successful commit — invalidating first would repopulate the
    /// cache from a state the database never accepted. Uses the existing graph invalidation:
    /// the product page and featured list carry availability, while the admin product list is
    /// keyed by id. Brand/category counts are deliberately untouched because a stock movement
    /// cannot change how many products exist.
    /// </summary>
    public async Task InvalidateCachesAsync(IReadOnlyList<Guid> productIds, CancellationToken ct)
    {
        if (productIds.Count == 0) return;

        var slugs = await db.Products
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Slug })
            .ToListAsync(ct);

        foreach (var product in slugs)
        {
            // Stock graph rather than individual keys: it covers the storefront product page and
            // the featured list, the only two projections that embed availability. Restoring stock
            // changes no counts, so brand/category ProductCounts stay valid.
            cache.InvalidateStockGraph(product.Slug);
        }
    }
}
