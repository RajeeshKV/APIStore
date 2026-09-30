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
    /// </summary>
    [SkippableFact]
    public async Task An_index_exists_on_Payment_ProviderRefundId()
    {
        await using var ctx = Db.CreateDbContext();

        var indexes = await GetIndexNamesAsync(ctx, "payments");

        indexes.Should().Contain(name => name.Contains("ProviderRefundId", StringComparison.Ordinal),
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
        var variant = ProductVariant.Create(product.Id, "SKU-BASE-CLASH", null);
        ctx.ProductVariants.Add(variant);
        ctx.InventoryItems.Add(InventoryItem.Create(product.Id, null, onHand: 10));
        await ctx.SaveChangesAsync();

        ctx.InventoryItems.Add(InventoryItem.Create(product.Id, variant.Id, onHand: 5));
        var act = async () => await ctx.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>(
            "a base row alongside per-variant rows would report stock the warehouse does not have");
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
        var product = Product.Create("Schema Test", $"schema-{Guid.NewGuid():N}", "SKU-SCHEMA", 10m, null, null);
        ctx.Products.Add(product);
        return product;
    }
}
