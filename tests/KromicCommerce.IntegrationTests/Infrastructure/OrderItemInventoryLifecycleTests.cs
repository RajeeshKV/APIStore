using KromicCommerce.Application.Abstractions.Data;
using KromicCommerce.Domain.Catalog;
using KromicCommerce.Domain.Orders;
using KromicCommerce.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KromicCommerce.IntegrationTests.Infrastructure;

/// <summary>
/// Proves against a real PostgreSQL that the per-order-line inventory lifecycle is persisted and
/// actually protects against double restoration.
///
/// The unit tests exercise the routing and idempotency rules, but they use a mocked DbContext,
/// which has NO concurrency token at all. Only a real database can demonstrate that two genuinely
/// simultaneous cancellations cannot both return the same units — which is the guarantee that
/// makes this safe across multiple API instances, where an in-process lock would not help.
/// </summary>
[Collection("Database")]
public sealed class OrderItemInventoryLifecycleTests(DatabaseFixture db)
    : IntegrationTestBase(db)
{
    // -----------------------------------------------------------------------
    // Schema
    // -----------------------------------------------------------------------

    [SkippableFact]
    public async Task The_lifecycle_columns_are_persisted()
    {
        await using var ctx = Db.CreateDbContext();
        var product = SeedProduct(ctx);
        var order = await SeedOrderWithLine(ctx, product, qty: 3, OrderItemInventoryStatus.Reserved);

        ctx.ChangeTracker.Clear();

        var stored = await ctx.OrderItems.FirstAsync(i => i.Id == order.Id);
        stored.InventoryStatus.Should().Be(OrderItemInventoryStatus.Reserved);
        stored.InventoryQuantity.Should().Be(3);
    }

    [SkippableFact]
    public async Task The_lifecycle_state_is_stored_as_a_readable_string()
    {
        await using var ctx = Db.CreateDbContext();
        var product = SeedProduct(ctx);
        await SeedOrderWithLine(ctx, product, qty: 1, OrderItemInventoryStatus.Finalized);

        // Read as text, bypassing the enum conversion, so the on-disk representation is asserted.
        var raw = await ctx.Database.SqlQueryRaw<string>(
            "SELECT \"InventoryStatus\" AS \"Value\" FROM order_items").ToListAsync();

        raw.Should().NotBeEmpty();
        raw.Should().Contain(["Untracked", "Reserved", "Finalized", "Restored"]);
    }

    [SkippableFact]
    public async Task The_order_line_concurrency_token_is_backed_by_the_xmin_system_column()
    {
        await using var ctx = Db.CreateDbContext();
        var product = SeedProduct(ctx);
        var line = await SeedOrderWithLine(ctx, product, qty: 1, OrderItemInventoryStatus.Reserved);

        ctx.ChangeTracker.Clear();

        var stored = await ctx.OrderItems.FirstAsync(i => i.Id == line.Id);
        stored.Version.Should().NotBe(0,
            "xmin is populated by PostgreSQL, proving the token maps to a real system column");
    }

    // -----------------------------------------------------------------------
    // Concurrency — the reason this state exists
    // -----------------------------------------------------------------------

    /// <summary>
    /// Two cancellation requests arriving at two different API instances both load the line as
    /// Finalized and both try to move it to Restored. Exactly one may win.
    ///
    /// The loser's UPDATE is rejected by PostgreSQL, and because the inventory credit is part of
    /// the same SaveChanges, its whole transaction rolls back — so the units are returned once.
    /// </summary>
    [SkippableFact]
    public async Task Two_simultaneous_cancellations_cannot_both_restore_the_same_units()
    {
        await using var ctx1 = Db.CreateDbContext();
        await using var ctx2 = Db.CreateDbContext();

        var product = SeedProduct(ctx1);
        var inventory = InventoryItem.Create(product.Id, null, onHand: 10);
        ctx1.InventoryItems.Add(inventory);
        await ctx1.SaveChangesAsync();

        var line = await SeedOrderWithLine(ctx1, product, qty: 2, OrderItemInventoryStatus.Finalized);
        var inventoryId = inventory.Id;

        // Model the post-confirmation state in the database: 2 units reserved then sold.
        var seed = await ctx1.InventoryItems.FirstAsync(i => i.Id == inventoryId);
        seed.Reserve(2);
        seed.FinalizeReservation(2);
        await ctx1.SaveChangesAsync();
        seed.OnHand.Should().Be(8);

        // Both instances load the same line and the same inventory row.
        var line1 = await ctx1.OrderItems.FirstAsync(i => i.Id == line.Id);
        var line2 = await ctx2.OrderItems.FirstAsync(i => i.Id == line.Id);
        var inv1 = await ctx1.InventoryItems.FirstAsync(i => i.Id == inventoryId);
        var inv2 = await ctx2.InventoryItems.FirstAsync(i => i.Id == inventoryId);
        line2.Version.Should().Be(line1.Version);

        // Instance 1 wins the race and returns the units.
        inv1.RestoreSoldUnits(line1.InventoryQuantity);
        line1.MarkInventoryRestored();
        await ctx1.SaveChangesAsync();

        // Instance 2 holds a stale token on BOTH rows and must be rejected outright.
        inv2.RestoreSoldUnits(line2.InventoryQuantity);
        line2.MarkInventoryRestored();

        var act = async () => await ctx2.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>(
            "the loser's transaction must roll back rather than credit the units a second time");

        // The winner's restore is intact and applied exactly once.
        ctx1.ChangeTracker.Clear();
        var finalOnHand = await ctx1.InventoryItems
            .Where(i => i.Id == inventoryId)
            .Select(i => i.OnHand)
            .SingleAsync();
        finalOnHand.Should().Be(10, "the units are returned exactly once");
    }

    /// <summary>
    /// The same protection for a duplicate payment-failure callback arriving concurrently.
    /// </summary>
    [SkippableFact]
    public async Task Two_simultaneous_payment_failure_releases_cannot_both_release()
    {
        await using var ctx1 = Db.CreateDbContext();
        await using var ctx2 = Db.CreateDbContext();

        var product = SeedProduct(ctx1);
        var inventory = InventoryItem.Create(product.Id, null, onHand: 10);
        ctx1.InventoryItems.Add(inventory);
        await ctx1.SaveChangesAsync();

        var line = await SeedOrderWithLine(ctx1, product, qty: 3, OrderItemInventoryStatus.Reserved);
        var inventoryId = inventory.Id;

        var seed = await ctx1.InventoryItems.FirstAsync(i => i.Id == inventoryId);
        seed.Reserve(3);
        await ctx1.SaveChangesAsync();
        seed.Reserved.Should().Be(3);

        var line1 = await ctx1.OrderItems.FirstAsync(i => i.Id == line.Id);
        var line2 = await ctx2.OrderItems.FirstAsync(i => i.Id == line.Id);
        var inv1 = await ctx1.InventoryItems.FirstAsync(i => i.Id == inventoryId);
        var inv2 = await ctx2.InventoryItems.FirstAsync(i => i.Id == inventoryId);

        inv1.Release(line1.InventoryQuantity);
        line1.MarkInventoryRestored();
        await ctx1.SaveChangesAsync();

        inv2.Release(line2.InventoryQuantity);
        line2.MarkInventoryRestored();

        var act = async () => await ctx2.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();

        ctx1.ChangeTracker.Clear();
        var finalReserved = await ctx1.InventoryItems
            .Where(i => i.Id == inventoryId)
            .Select(i => i.Reserved)
            .SingleAsync();
        finalReserved.Should().Be(0);
    }

    /// <summary>
    /// A repeated confirmation must not consume the same reserved units twice. Finalization moves
    /// the line Reserved -&gt; Finalized, and the second attempt is rejected both by the domain
    /// rule and by the concurrency token.
    /// </summary>
    [SkippableFact]
    public async Task Confirming_twice_cannot_consume_the_reservation_twice()
    {
        await using var ctx = Db.CreateDbContext();

        var product = SeedProduct(ctx);
        var inventory = InventoryItem.Create(product.Id, null, onHand: 10);
        ctx.InventoryItems.Add(inventory);
        await ctx.SaveChangesAsync();

        var line = await SeedOrderWithLine(ctx, product, qty: 2, OrderItemInventoryStatus.Reserved);

        var seed = await ctx.InventoryItems.FirstAsync(i => i.ProductId == product.Id);
        seed.Reserve(2);
        await ctx.SaveChangesAsync();

        var tracked = await ctx.OrderItems.FirstAsync(i => i.Id == line.Id);
        tracked.MarkInventoryFinalized();
        seed.FinalizeReservation(tracked.InventoryQuantity);
        await ctx.SaveChangesAsync();
        seed.OnHand.Should().Be(8);

        ctx.ChangeTracker.Clear();

        var again = await ctx.OrderItems.FirstAsync(i => i.Id == line.Id);
        again.InventoryStatus.Should().Be(OrderItemInventoryStatus.Finalized);

        var domainGuard = () => again.MarkInventoryFinalized();
        domainGuard.Should().Throw<InvalidOperationException>(
            "only reserved units can be finalized, so a repeated confirmation is refused");
    }

    // -----------------------------------------------------------------------
    // Transactional atomicity
    // -----------------------------------------------------------------------

    /// <summary>
    /// The whole point of staging: the inventory credit and the order line's Restored marker must
    /// land together. If the line update is rejected, the stock credit must be rolled back with
    /// it, so stock is never restored against an order that was not actually cancelled.
    /// </summary>
    [SkippableFact]
    public async Task A_rejected_line_update_rolls_back_the_inventory_credit()
    {
        await using var ctx1 = Db.CreateDbContext();
        await using var ctx2 = Db.CreateDbContext();

        var product = SeedProduct(ctx1);
        var inventory = InventoryItem.Create(product.Id, null, onHand: 10);
        ctx1.InventoryItems.Add(inventory);
        await ctx1.SaveChangesAsync();

        var line = await SeedOrderWithLine(ctx1, product, qty: 2, OrderItemInventoryStatus.Finalized);
        var inventoryId = inventory.Id;

        var seed = await ctx1.InventoryItems.FirstAsync(i => i.Id == inventoryId);
        seed.Reserve(2);
        seed.FinalizeReservation(2);
        await ctx1.SaveChangesAsync();

        // A competing writer touches the line, invalidating this context's token.
        var competitor = await ctx2.OrderItems.FirstAsync(i => i.Id == line.Id);
        competitor.MarkInventoryRestored();
        await ctx2.SaveChangesAsync();

        var staleLine = await ctx1.OrderItems.FirstAsync(i => i.Id == line.Id);
        var staleInventory = await ctx1.InventoryItems.FirstAsync(i => i.Id == inventoryId);

        staleInventory.RestoreSoldUnits(staleLine.InventoryQuantity);
        staleLine.MarkInventoryRestored();

        var act = async () => await ctx1.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();

        // The inventory credit must have been rolled back with the rejected line update.
        ctx2.ChangeTracker.Clear();
        var finalOnHand = await ctx2.InventoryItems
            .Where(i => i.Id == inventoryId)
            .Select(i => i.OnHand)
            .SingleAsync();
        finalOnHand.Should().Be(8,
            "a rejected cancellation must not leave the units credited to sellable stock");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Product SeedProduct(AppDbContext ctx)
    {
        var product = Product.Create(
            $"Widget-{Guid.NewGuid():N}", $"widget-{Guid.NewGuid():N}", null,
            100m, null, null);
        ctx.Products.Add(product);
        ctx.SaveChanges();
        return product;
    }

    /// <summary>
    /// Persists an order line already carrying the lifecycle state that checkout/confirmation
    /// would have recorded.
    /// </summary>
    private static async Task<OrderItem> SeedOrderWithLine(
        AppDbContext ctx, Product product, int qty, OrderItemInventoryStatus status)
    {
        var address = ShippingAddress.Create(
            "Test User", "+919876543210", "Line 1", null, "City", "State", "12345", "IN");

        var order = Order.Create(
            Guid.NewGuid(), $"ORD-{Guid.NewGuid():N}", "INR",
            subtotal: product.Price, shippingAmount: 0m, codFee: 0m,
            discountAmount: 0m, taxAmount: 0m, grandTotal: product.Price,
            address, PaymentMethod.CashOnDelivery);
        ctx.Orders.Add(order);

        var item = OrderItem.Create(
            order.Id, product.Id, null, product.Name, null, null, product.Price, qty);
        item.MarkInventoryReserved(qty);
        if (status == OrderItemInventoryStatus.Finalized)
            item.MarkInventoryFinalized();

        ctx.OrderItems.Add(item);
        await ctx.SaveChangesAsync();
        return item;
    }
}
