using System.Linq.Expressions;
using KromicCommerce.Application.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace KromicCommerce.UnitTests.Application;

/// <summary>
/// Proves that inventory restoration is driven by the PERSISTED per-order-line lifecycle and is
/// genuinely idempotent.
///
/// The defect these guard: the previous implementation inferred what to restore from the current
/// Reserved/OnHand counters. That is unsound in two ways — it can misattribute another order's
/// reservation, and it has no memory of whether this order's units were already returned, so a
/// repeated cancellation could return them twice.
/// </summary>
public sealed class OrderInventoryRestoreIdempotencyTests
{
    private readonly Mock<IApplicationDbContext> _db = new();
    private readonly Mock<ICatalogCacheService> _cache = new();

    // -----------------------------------------------------------------------
    // Sequential idempotency
    // -----------------------------------------------------------------------

    /// <summary>
    /// The headline requirement: calling cancellation twice must return the units exactly once.
    /// </summary>
    [Fact]
    public async Task Cancelling_twice_restores_inventory_only_once()
    {
        var productId = Guid.NewGuid();
        var order = BuildOrder();
        var line = AddLine(order, productId, qty: 2, OrderItemInventoryStatus.Finalized);

        // OnHand 8 / Reserved 0 — two units already sold.
        var inventory = InventoryItem.Create(productId, null, onHand: 10);
        inventory.Reserve(2);
        inventory.FinalizeReservation(2);
        inventory.OnHand.Should().Be(8);

        SetupInventory(inventory);
        var restorer = BuildRestorer();

        // First cancellation.
        var first = await restorer.StageRestoreAsync(order, CancellationToken.None);
        inventory.OnHand.Should().Be(10);
        inventory.Reserved.Should().Be(0);
        line.InventoryStatus.Should().Be(OrderItemInventoryStatus.Restored);
        first.Should().ContainSingle();

        // Second cancellation of the same order must add nothing.
        var second = await restorer.StageRestoreAsync(order, CancellationToken.None);
        inventory.OnHand.Should().Be(10, "a repeated restore must not credit the units twice");
        inventory.Available.Should().Be(10);
        second.Should().BeEmpty("a fully restored order has no stock left to report as changed");
    }

    /// <summary>
    /// A duplicate payment-failure callback is the same hazard on the release path. The gateway
    /// may retry a webhook, so the release must survive being driven twice.
    /// </summary>
    [Fact]
    public async Task A_duplicate_payment_failure_release_does_not_double_release()
    {
        var productId = Guid.NewGuid();
        var order = BuildOrder();
        var line = AddLine(order, productId, qty: 3, OrderItemInventoryStatus.Reserved);

        var inventory = InventoryItem.Create(productId, null, onHand: 10);
        inventory.Reserve(3);
        SetupInventory(inventory);

        var restorer = BuildRestorer();

        await restorer.StageRestoreAsync(order, CancellationToken.None);
        inventory.Reserved.Should().Be(0);
        inventory.OnHand.Should().Be(10, "reserved units were never removed from OnHand");

        await restorer.StageRestoreAsync(order, CancellationToken.None);
        inventory.Reserved.Should().Be(0);
        inventory.OnHand.Should().Be(10, "the duplicate callback must not release anything again");
        line.InventoryStatus.Should().Be(OrderItemInventoryStatus.Restored);
    }

    // -----------------------------------------------------------------------
    // The routing decision comes from persisted state, not current counters
    // -----------------------------------------------------------------------

    /// <summary>
    /// A Finalized line adds its units back to OnHand. If this were routed through the reserved
    /// path instead, the units would be stranded (and a later cancellation would find nothing).
    /// </summary>
    [Fact]
    public async Task A_finalized_line_is_restored_by_adding_back_to_on_hand()
    {
        var productId = Guid.NewGuid();
        var order = BuildOrder();
        AddLine(order, productId, qty: 2, OrderItemInventoryStatus.Finalized);

        var inventory = InventoryItem.Create(productId, null, onHand: 10);
        inventory.Reserve(2);
        inventory.FinalizeReservation(2);
        SetupInventory(inventory);

        await BuildRestorer().StageRestoreAsync(order, CancellationToken.None);

        inventory.OnHand.Should().Be(10);
        inventory.Reserved.Should().Be(0);
    }

    /// <summary>
    /// A Reserved line only leaves the Reserved bucket. Adding it to OnHand as well would inflate
    /// sellable stock, because those units were never taken out of it.
    /// </summary>
    [Fact]
    public async Task A_reserved_line_is_restored_without_touching_on_hand()
    {
        var productId = Guid.NewGuid();
        var order = BuildOrder();
        AddLine(order, productId, qty: 2, OrderItemInventoryStatus.Reserved);

        var inventory = InventoryItem.Create(productId, null, onHand: 10);
        inventory.Reserve(2);
        SetupInventory(inventory);

        await BuildRestorer().StageRestoreAsync(order, CancellationToken.None);

        inventory.OnHand.Should().Be(10, "these units were always inside OnHand");
        inventory.Reserved.Should().Be(0);
    }

    /// <summary>
    /// The exact scenario the old inference got wrong. Order A sold 2 units; the warehouse then
    /// restocked and Order B reserved 3. Restoring A by inferring from Reserved would consume 2 of
    /// Order B's reservation. Persisted state routes A's units correctly and leaves B alone.
    /// </summary>
    [Fact]
    public async Task Restoring_one_order_does_not_disturb_another_orders_reservation()
    {
        var productA = Guid.NewGuid();
        var orderA = BuildOrder();
        AddLine(orderA, productA, qty: 2, OrderItemInventoryStatus.Finalized);

        var inventory = InventoryItem.Create(productA, null, onHand: 10);
        inventory.Reserve(2);
        inventory.FinalizeReservation(2);   // Order A sells 2  -> OnHand 8, Reserved 0
        inventory.SetOnHand(10);            // restock          -> OnHand 10
        inventory.Reserve(3);               // Order B reserves -> Reserved 3

        SetupInventory(inventory);

        await BuildRestorer().StageRestoreAsync(orderA, CancellationToken.None);

        inventory.OnHand.Should().Be(12, "Order A's 2 sold units are physically back");
        inventory.Reserved.Should().Be(3, "Order B's reservation is untouched");
    }

    // -----------------------------------------------------------------------
    // Atomicity across multiple lines
    // -----------------------------------------------------------------------

    /// <summary>
    /// If any line cannot be restored, NONE may be mutated. A partial restore would leave the
    /// order unrecoverable: it can only be cancelled once.
    /// </summary>
    [Fact]
    public async Task One_unrestorable_line_aborts_the_whole_restore()
    {
        var goodProduct = Guid.NewGuid();
        var missingProduct = Guid.NewGuid();

        var order = BuildOrder();
        AddLine(order, goodProduct, qty: 2, OrderItemInventoryStatus.Finalized);
        var badLine = AddLine(order, missingProduct, qty: 1, OrderItemInventoryStatus.Finalized);

        // Only the first product has an inventory row; the second row was deleted.
        var inventory = InventoryItem.Create(goodProduct, null, onHand: 10);
        inventory.Reserve(2);
        inventory.FinalizeReservation(2);
        SetupInventory(inventory);

        var act = () => BuildRestorer().StageRestoreAsync(order, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        inventory.OnHand.Should().Be(8, "the good line must not have been credited");
        badLine.InventoryStatus.Should().Be(OrderItemInventoryStatus.Finalized,
            "no line may be marked restored when the operation aborts");
    }

    /// <summary>
    /// A reserved line whose reservation has been consumed by something else is a hard error, not
    /// a silent skip — the units are unaccounted for and only a human can reconcile that.
    /// </summary>
    [Fact]
    public async Task A_reserved_line_with_no_matching_reservation_fails_loudly()
    {
        var productId = Guid.NewGuid();
        var order = BuildOrder();
        AddLine(order, productId, qty: 4, OrderItemInventoryStatus.Reserved);

        var inventory = InventoryItem.Create(productId, null, onHand: 10);
        inventory.Reserve(1); // only 1 of the recorded 4 is still held
        SetupInventory(inventory);

        var act = () => BuildRestorer().StageRestoreAsync(order, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*reserved*");
    }

    /// <summary>
    /// Products created before inventory tracking have no row. They are legitimately untracked and
    /// must never block a cancellation.
    /// </summary>
    [Fact]
    public async Task An_untracked_line_is_skipped_and_does_not_block_cancellation()
    {
        var order = BuildOrder();
        order.AddItem(OrderItem.Create(
            order.Id, Guid.NewGuid(), null, "Legacy", null, "SKU-X", 100m, 1));

        SetupInventory(); // no inventory rows at all

        var act = () => BuildRestorer().StageRestoreAsync(order, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    /// <summary>
    /// Mixed orders are the real-world case: some lines confirmed, some still awaiting
    /// confirmation. Each must take its own path, and both must end fully restored.
    /// </summary>
    [Fact]
    public async Task Mixed_lines_restore_via_their_own_recorded_state()
    {
        var confirmedProduct = Guid.NewGuid();
        var pendingProduct = Guid.NewGuid();

        var order = BuildOrder();
        var soldLine = AddLine(order, confirmedProduct, qty: 2, OrderItemInventoryStatus.Finalized);
        var heldLine = AddLine(order, pendingProduct, qty: 3, OrderItemInventoryStatus.Reserved);

        var sold = InventoryItem.Create(confirmedProduct, null, onHand: 10);
        sold.Reserve(2);
        sold.FinalizeReservation(2);

        var held = InventoryItem.Create(pendingProduct, null, onHand: 10);
        held.Reserve(3);

        SetupInventory(sold, held);

        var affected = await BuildRestorer().StageRestoreAsync(order, CancellationToken.None);

        sold.OnHand.Should().Be(10);
        sold.Reserved.Should().Be(0);
        held.OnHand.Should().Be(10);
        held.Reserved.Should().Be(0);
        soldLine.InventoryStatus.Should().Be(OrderItemInventoryStatus.Restored);
        heldLine.InventoryStatus.Should().Be(OrderItemInventoryStatus.Restored);
        affected.Should().BeEquivalentTo([confirmedProduct, pendingProduct]);
    }

    // -----------------------------------------------------------------------
    // Cache invalidation
    // -----------------------------------------------------------------------

    /// <summary>
    /// Stock changed, so the cached availability projections must be evicted — otherwise the
    /// storefront keeps advertising stock that was just returned.
    /// </summary>
    [Fact]
    public async Task Restoring_stock_invalidates_the_availability_projections()
    {
        var productId = Guid.NewGuid();
        var order = BuildOrder();
        AddLine(order, productId, qty: 2, OrderItemInventoryStatus.Finalized);

        var inventory = InventoryItem.Create(productId, null, onHand: 10);
        inventory.Reserve(2);
        inventory.FinalizeReservation(2);
        SetupInventory(inventory, productId);

        var restorer = BuildRestorer();
        var affected = await restorer.StageRestoreAsync(order, CancellationToken.None);
        await restorer.InvalidateCachesAsync(affected, CancellationToken.None);

        _cache.Verify(c => c.InvalidateStockGraph("test-widget"), Times.Once);
        // Brand/category counts are unaffected by a stock movement and must not be evicted.
        _cache.Verify(c => c.InvalidateBrandGraph(), Times.Never);
        _cache.Verify(c => c.InvalidateCategoryGraph(), Times.Never);
        // The stock graph is the whole invalidation. Individual key removal is not used here:
        // the admin per-product key is never written by any read path, so evicting it was a
        // no-op that only obscured which projections actually embed availability.
        _cache.Verify(c => c.InvalidateProduct(It.IsAny<Guid>()), Times.Never);
        _cache.Verify(c => c.InvalidateProductGraph(It.IsAny<Guid>(), It.IsAny<string?>()), Times.Never);
    }

    /// <summary>
    /// A restore that changes nothing must not churn the cache. An order whose only line is
    /// untracked has no stock to return, so there is nothing to evict.
    /// </summary>
    [Fact]
    public async Task An_order_with_nothing_to_restore_does_not_invalidate_anything()
    {
        var order = BuildOrder();
        order.AddItem(OrderItem.Create(
            order.Id, Guid.NewGuid(), null, "Legacy", null, "SKU-X", 100m, 1));

        SetupInventory();

        var restorer = BuildRestorer();
        var affected = await restorer.StageRestoreAsync(order, CancellationToken.None);
        await restorer.InvalidateCachesAsync(affected, CancellationToken.None);

        affected.Should().BeEmpty();
        _cache.Verify(c => c.InvalidateStockGraph(It.IsAny<string?>()), Times.Never);
        _cache.Verify(c => c.InvalidateProduct(It.IsAny<Guid>()), Times.Never);
        _cache.Verify(c => c.InvalidateProductGraph(It.IsAny<Guid>(), It.IsAny<string?>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // Domain-level guards behind the database token
    // -----------------------------------------------------------------------

    /// <summary>
    /// The in-memory guard that turns a sequential double restore into a loud error rather than
    /// a silent double-credit. The database token is what protects genuinely concurrent writes.
    /// </summary>
    [Fact]
    public void Marking_a_line_restored_twice_is_rejected()
    {
        var line = OrderItem.Create(
            Guid.NewGuid(), Guid.NewGuid(), null, "Widget", null, "SKU-1", 100m, 2);
        line.MarkInventoryReserved(2);
        line.MarkInventoryFinalized();

        line.CanRestoreInventory.Should().BeTrue();
        line.MarkInventoryRestored();

        line.CanRestoreInventory.Should().BeFalse(
            "a restored line has nothing left to give back");
        var act = () => line.MarkInventoryRestored();
        act.Should().Throw<InvalidOperationException>();
    }

    /// <summary>
    /// Only reserved units can be finalized. Without this, a repeated confirmation could consume
    /// the same units twice.
    /// </summary>
    [Fact]
    public void Only_a_reserved_line_can_be_finalized()
    {
        var line = OrderItem.Create(
            Guid.NewGuid(), Guid.NewGuid(), null, "Widget", null, "SKU-1", 100m, 1);

        // Untracked lines have no units to finalize.
        var onUntracked = () => line.MarkInventoryFinalized();
        onUntracked.Should().Throw<InvalidOperationException>();

        line.MarkInventoryReserved(1);
        line.MarkInventoryFinalized();

        var again = () => line.MarkInventoryFinalized();
        again.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_line_cannot_be_reserved_twice()
    {
        var line = OrderItem.Create(
            Guid.NewGuid(), Guid.NewGuid(), null, "Widget", null, "SKU-1", 100m, 1);

        line.MarkInventoryReserved(1);

        var act = () => line.MarkInventoryReserved(1);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void An_untracked_line_reports_that_it_does_not_track_inventory()
    {
        var line = OrderItem.Create(
            Guid.NewGuid(), Guid.NewGuid(), null, "Legacy", null, "SKU-X", 100m, 4);

        line.TracksInventory.Should().BeFalse();
        line.CanRestoreInventory.Should().BeFalse();
        line.InventoryQuantity.Should().Be(0);
    }

    /// <summary>
    /// The quantity recorded at reservation is the quantity handed back later, even if it is not
    /// the line's own Quantity. Restoration must use the recorded figure.
    /// </summary>
    [Fact]
    public void The_recorded_inventory_quantity_is_what_gets_restored()
    {
        var line = OrderItem.Create(
            Guid.NewGuid(), Guid.NewGuid(), null, "Widget", null, "SKU-1", 100m, 5);

        line.MarkInventoryReserved(3);

        line.InventoryQuantity.Should().Be(3);
        line.Quantity.Should().Be(5, "the order line quantity is a commercial figure and is independent");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private OrderInventoryRestorer BuildRestorer()
        => new(_db.Object, _cache.Object, NullLogger<OrderInventoryRestorer>.Instance);

    private static Order BuildOrder()
    {
        var address = ShippingAddress.Create(
            "Test User", "+919876543210", "Line 1", null, "City", "State", "12345", "IN");

        var order = Order.Create(
            Guid.NewGuid(), "ORD-IDEMPOTENCY", "INR",
            subtotal: 100m, shippingAmount: 0m, codFee: 0m,
            discountAmount: 0m, taxAmount: 0m, grandTotal: 100m,
            address, PaymentMethod.Razorpay);

        typeof(Entity).GetProperty("Id")!.SetValue(order, Guid.NewGuid());
        order.Confirm();
        return order;
    }

    /// <summary>
    /// Builds a line already carrying the lifecycle state checkout + confirmation would persist.
    /// </summary>
    private static OrderItem AddLine(
        Order order, Guid productId, int qty, OrderItemInventoryStatus status)
    {
        var item = OrderItem.Create(
            order.Id, productId, null, "Widget", null, "SKU-1", 100m, qty);

        item.MarkInventoryReserved(qty);
        if (status == OrderItemInventoryStatus.Finalized)
            item.MarkInventoryFinalized();

        order.AddItem(item);
        return item;
    }

    private void SetupInventory(params InventoryItem[] items)
    {
        SetupSet(_db, d => d.InventoryItems, [.. items]);
        SetupSet(_db, d => d.Products, []);
    }

    /// <summary>
    /// Products are consulted only to resolve slugs for cache invalidation.
    /// </summary>
    private void SetupInventory(InventoryItem inventory, params Guid[] productIds)
    {
        SetupSet(_db, d => d.InventoryItems, [inventory]);
        SetupSet(_db, d => d.Products,
            productIds.Select(id => CreateProduct(id)).ToList());
    }

    private static Product CreateProduct(Guid id)
    {
        var product = Product.Create(
            name: "Widget", slug: "test-widget", sku: "SKU-1",
            price: 100m, categoryId: null, brandId: null);

        typeof(Entity).GetProperty("Id")!.SetValue(product, id);
        return product;
    }

    private static void SetupSet<T>(
        Mock<IApplicationDbContext> db,
        Expression<Func<IApplicationDbContext, DbSet<T>>> selector,
        List<T> data)
        where T : class
    {
        var queryable = data.AsQueryable();
        var mock = new Mock<DbSet<T>>();

        mock.As<IAsyncEnumerable<T>>()
            .Setup(m => m.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns(new TestAsyncEnumerator<T>(queryable.GetEnumerator()));
        mock.As<IQueryable<T>>().Setup(m => m.Provider)
            .Returns(new TestAsyncQueryProvider<T>(queryable.Provider));
        mock.As<IQueryable<T>>().Setup(m => m.Expression).Returns(queryable.Expression);
        mock.As<IQueryable<T>>().Setup(m => m.ElementType).Returns(queryable.ElementType);
        mock.As<IQueryable<T>>().Setup(m => m.GetEnumerator()).Returns(queryable.GetEnumerator());

        db.Setup(selector).Returns(mock.Object);
    }
}
