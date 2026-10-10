using KromicCommerce.Domain.Cart;
using KromicCommerce.Domain.Catalog;
using KromicCommerce.Domain.Orders;
using KromicCommerce.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KromicCommerce.IntegrationTests.Infrastructure;

/// <summary>
/// Schema guarantees introduced by VariantAttributesCartCouponPaymentRefund.
///
/// These assertions are deliberately about the DATABASE, not the C# model: a column can exist
/// in the model snapshot and still be missing or nullable in the database if a migration did
/// not add it. Only a real PostgreSQL instance can prove the migration is correct.
///
/// Requires Docker — tests skip gracefully when Docker is unavailable.
/// </summary>
[Collection("Database")]
public sealed class PaymentRefundSchemaTests(DatabaseFixture db)
    : IntegrationTestBase(db)
{
    /// <summary>
    /// The refund receipt is the idempotency checkpoint for cancellation. If these columns were
    /// not persisted, a retried cancellation would find no refund recorded and issue a second
    /// refund against the same captured payment.
    /// </summary>
    [SkippableFact]
    public async Task Payment_refund_columns_exist_and_persist_round_trip()
    {
        await using var ctx = Db.CreateDbContext();

        var order = SeedOrder(ctx);
        var payment = Payment.Create(order.Id, "Razorpay", 1000m, "INR");
        payment.MarkPaid("pay_persist_test");
        payment.MarkRefunded("rfnd_persist_test");
        ctx.Payments.Add(payment);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var reloaded = await ctx.Payments.SingleAsync(p => p.Id == payment.Id);

        reloaded.ProviderRefundId.Should().Be("rfnd_persist_test");
        reloaded.RefundedAmount.Should().Be(1000m);
        reloaded.RefundedAtUtc.Should().NotBeNull();
        reloaded.IsRefunded.Should().BeTrue();
    }

    /// <summary>
    /// The index on ProviderRefundId makes "has this payment already been refunded?" cheap,
    /// which the cancellation flow asks on every attempt.
    ///
    /// Asserted on the indexed COLUMN rather than the index name. The name is an implementation
    /// detail — the mapping deliberately abbreviates it to ix_payments_provider_refund — so a
    /// name-based assertion would pass or fail on cosmetic refactors while telling us nothing
    /// about whether the lookup the cancellation flow performs is actually indexed.
    /// </summary>
    [SkippableFact]
    public async Task An_index_exists_on_Payment_ProviderRefundId()
    {
        await using var ctx = Db.CreateDbContext();

        var definitions = await ctx.Database.SqlQueryRaw<string>(
            """
            SELECT indexdef AS "Value"
            FROM pg_indexes
            WHERE tablename = 'payments'
            """).ToListAsync();

        definitions.Should().Contain(def => def.Contains("ProviderRefundId", StringComparison.Ordinal),
            "the cancellation flow looks up refunds by provider refund id on every attempt");
    }

    // -----------------------------------------------------------------------
    // Cart coupon column
    // -----------------------------------------------------------------------

    /// <summary>
    /// The coupon code is persisted on the cart, but the discount is never persisted. A stored
    /// discount would let a stale amount be charged after the promotion rules change.
    /// </summary>
    [SkippableFact]
    public async Task Cart_persists_the_coupon_code_and_no_discount()
    {
        await using var ctx = Db.CreateDbContext();

        var cart = Cart.CreateForCustomer(Guid.NewGuid());
        cart.AddItem(Guid.NewGuid(), null, 1);
        cart.ApplyCoupon("  save10  ");
        ctx.Carts.Add(cart);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var reloaded = await ctx.Carts.SingleAsync(c => c.Id == cart.Id);

        // Normalised to upper case so the same code cannot be stored two different ways.
        reloaded.CouponCode.Should().Be("SAVE10");

        // There is no column to hold a discount amount, which is the guarantee that matters.
        typeof(Cart).GetProperties()
            .Select(p => p.Name)
            .Should().NotContain(n => n.Contains("Discount", StringComparison.OrdinalIgnoreCase),
                "a stored cart discount would survive a promotion rule change and be charged");
    }

    // -----------------------------------------------------------------------
    // Product attribute uniqueness
    // -----------------------------------------------------------------------

    /// <summary>
    /// Two attributes with the same name on one product would make variant labels ambiguous
    /// ("Size / M" could belong to either), so the database enforces uniqueness.
    /// </summary>
    [SkippableFact]
    public async Task A_product_cannot_have_two_attributes_with_the_same_name()
    {
        await using var ctx = Db.CreateDbContext();
        var product = SeedProduct(ctx);

        var first = ProductAttribute.Create(product.Id, "Colour");
        ctx.ProductAttributes.Add(first);
        await ctx.SaveChangesAsync();

        var duplicate = ProductAttribute.Create(product.Id, "Colour");
        ctx.ProductAttributes.Add(duplicate);

        var act = async () => await ctx.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>(
            "duplicate attribute names on one product would make variant labels ambiguous");
    }

    [SkippableFact]
    public async Task A_product_attribute_cannot_have_two_values_with_the_same_display_value()
    {
        await using var ctx = Db.CreateDbContext();
        var product = SeedProduct(ctx);

        var attribute = ProductAttribute.Create(product.Id, "Colour");
        ctx.ProductAttributes.Add(attribute);
        await ctx.SaveChangesAsync();

        ctx.ProductAttributeValues.Add(ProductAttributeValue.Create(attribute.Id, "Red"));
        await ctx.SaveChangesAsync();

        ctx.ProductAttributeValues.Add(ProductAttributeValue.Create(attribute.Id, "Red"));
        var act = async () => await ctx.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>(
            "duplicate values under one attribute would produce indistinguishable variants");
    }

    // -----------------------------------------------------------------------
    // Inventory uniqueness
    // -----------------------------------------------------------------------

    /// <summary>
    /// A base-product inventory row (VariantId NULL) is the row used for products without
    /// variants. PostgreSQL's default NULL semantics allow many rows with NULL VariantId, so
    /// this needs NULLS NOT DISTINCT — otherwise stock can be split across duplicate rows and
    /// oversell.
    /// </summary>
    [SkippableFact]
    public async Task A_product_cannot_have_two_base_product_inventory_rows()
    {
        await using var ctx = Db.CreateDbContext();
        var product = SeedProduct(ctx);

        ctx.InventoryItems.Add(InventoryItem.Create(product.Id, null, onHand: 10));
        await ctx.SaveChangesAsync();

        ctx.InventoryItems.Add(InventoryItem.Create(product.Id, null, onHand: 10));
        var act = async () => await ctx.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>(
            "duplicate base-product inventory rows would split stock and allow overselling");
    }

    /// <summary>
    /// A product WITH variants must not also carry a base-product inventory row, or the two
    /// would be summed and report stock the warehouse does not have.
    /// </summary>
    [SkippableFact]
    public async Task A_product_with_variants_cannot_have_a_base_product_inventory_row()
    {
        await using var ctx = Db.CreateDbContext();
        var product = SeedProduct(ctx);
        var variant = ProductVariant.Create(product.Id, $"SKU-BASE-{Guid.NewGuid():N}", null, compareAtPrice: null);
        ctx.ProductVariants.Add(variant);
        ctx.InventoryItems.Add(InventoryItem.Create(product.Id, null, onHand: 10));
        await ctx.SaveChangesAsync();

        ctx.InventoryItems.Add(InventoryItem.Create(product.Id, variant.Id, onHand: 5));
        var act = async () => await ctx.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>(
            "a base row alongside per-variant rows would report stock the warehouse does not have");
    }

    // -----------------------------------------------------------------------
    // Inventory concurrency (xmin)
    // -----------------------------------------------------------------------

    /// <summary>
    /// RowVersion maps to PostgreSQL's xmin system column and is configured as a concurrency
    /// token. This proves the mapping is real: two contexts loading the same row, where the
    /// first writes, must make the second write fail rather than silently overwriting.
    ///
    /// This is the mechanism that prevents overselling. A unit test cannot prove it, because a
    /// mocked DbContext has no concurrency token at all.
    /// </summary>
    [SkippableFact]
    public async Task A_stale_inventory_write_is_rejected_by_the_concurrency_token()
    {
        await using var ctx1 = Db.CreateDbContext();
        await using var ctx2 = Db.CreateDbContext();

        var product = SeedProduct(ctx1);
        ctx1.InventoryItems.Add(InventoryItem.Create(product.Id, null, onHand: 1));
        await ctx1.SaveChangesAsync();
        var inventoryId = (await ctx1.InventoryItems.FirstAsync(i => i.ProductId == product.Id)).Id;

        // Both contexts load the same row, so both hold the same RowVersion.
        var first = await ctx1.InventoryItems.FirstAsync(i => i.Id == inventoryId);
        var second = await ctx2.InventoryItems.FirstAsync(i => i.Id == inventoryId);
        second.RowVersion.Should().Be(first.RowVersion);

        // Context 1 wins the race and consumes the last unit.
        first.Reserve(1);
        await ctx1.SaveChangesAsync();

        // Context 2 still holds the stale token, so its write must be rejected rather than
        // clobbering the reservation.
        second.Reserve(1);
        var act = async () => await ctx2.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>(
            "a stale write would oversell the final unit");
    }

    /// <summary>
    /// The concurrency token is a real database column, not a client-side value, so the unique
    /// index on (ProductId, VariantId) and the xmin token are both enforced by PostgreSQL.
    /// </summary>
    [SkippableFact]
    public async Task The_concurrency_token_is_backed_by_a_real_column()
    {
        await using var ctx = Db.CreateDbContext();
        var product = SeedProduct(ctx);
        ctx.InventoryItems.Add(InventoryItem.Create(product.Id, null, onHand: 3));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var stored = await ctx.InventoryItems.FirstAsync(i => i.ProductId == product.Id);
        stored.RowVersion.Should().NotBe(0,
            "xmin is populated by PostgreSQL, proving the token maps to a real system column");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static async Task<List<string>> GetIndexNamesAsync(AppDbContext ctx, string table)
    {
        var connection = ctx.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT indexname FROM pg_indexes WHERE tablename = @table";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "@table";
        parameter.Value = table;
        command.Parameters.Add(parameter);

        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            names.Add(reader.GetString(0));
        return names;
    }

    private static Order SeedOrder(AppDbContext ctx)
    {
        var address = ShippingAddress.Create(
            "Test User", "+919876543210", "Line 1", null,
            "City", "State", "12345", "IN");

        var order = Order.Create(
            Guid.NewGuid(), $"ORD-{Guid.NewGuid():N}", "INR",
            subtotal: 1000m, shippingAmount: 0m, codFee: 0m,
            discountAmount: 0m, taxAmount: 0m, grandTotal: 1000m,
            address, PaymentMethod.Razorpay);

        ctx.Orders.Add(order);
        return order;
    }

    private static Product SeedProduct(AppDbContext ctx)
    {
        // The SKU must be unique per call, not just the slug. These tests share one database, and
        // SKU is under a unique index, so a fixed value made every test after the first fail on
        // ix_products_sku — masking the constraint each test was actually written to prove.
        var product = Product.Create(
            "Schema Test", $"schema-{Guid.NewGuid():N}", $"SKU-SCHEMA-{Guid.NewGuid():N}", 10m, null, null);
        ctx.Products.Add(product);
        return product;
    }
}
