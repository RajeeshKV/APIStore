namespace KromicCommerce.UnitTests.Domain;

public sealed class ProductVariantDomainTests
{
    private static readonly Guid ProductId = Guid.NewGuid();

    // -----------------------------------------------------------------------
    // Create
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_sets_all_fields_correctly()
    {
        var v = ProductVariant.Create(ProductId, "SKU-001", 99.99m, sortOrder: 1);
        v.ProductId.Should().Be(ProductId);
        v.Sku.Should().Be("SKU-001");
        v.PriceOverride.Should().Be(99.99m);
        v.SortOrder.Should().Be(1);
        v.IsActive.Should().BeTrue();
        v.AttributeValueIds.Should().BeNull();
    }

    [Fact]
    public void Create_trims_sku()
    {
        var v = ProductVariant.Create(ProductId, "  SKU-001  ", null, 0);
        v.Sku.Should().Be("SKU-001");
    }

    [Fact]
    public void Create_allows_null_sku()
    {
        var v = ProductVariant.Create(ProductId, null, null, 0);
        v.Sku.Should().BeNull();
    }

    [Fact]
    public void Create_allows_zero_price_override()
    {
        var v = ProductVariant.Create(ProductId, null, 0m, 0);
        v.PriceOverride.Should().Be(0m);
    }

    [Fact]
    public void Create_throws_for_negative_price_override()
    {
        var act = () => ProductVariant.Create(ProductId, null, -1m, 0);
        act.Should().Throw<ArgumentException>().WithMessage("*>= 0*");
    }

    // -----------------------------------------------------------------------
    // Update
    // -----------------------------------------------------------------------

    [Fact]
    public void Update_replaces_fields()
    {
        var v = ProductVariant.Create(ProductId, "OLD", 10m, 0);
        v.Update("NEW", 20m, 5);
        v.Sku.Should().Be("NEW");
        v.PriceOverride.Should().Be(20m);
        v.SortOrder.Should().Be(5);
    }

    [Fact]
    public void Update_throws_for_negative_price()
    {
        var v = ProductVariant.Create(ProductId, null, null, 0);
        var act = () => v.Update(null, -0.01m, 0);
        act.Should().Throw<ArgumentException>();
    }

    // -----------------------------------------------------------------------
    // Activate / Deactivate
    // -----------------------------------------------------------------------

    [Fact]
    public void Deactivate_sets_IsActive_false()
    {
        var v = ProductVariant.Create(ProductId, null, null, 0);
        v.Deactivate();
        v.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Activate_sets_IsActive_true()
    {
        var v = ProductVariant.Create(ProductId, null, null, 0);
        v.Deactivate();
        v.Activate();
        v.IsActive.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // SetAttributeValues
    // -----------------------------------------------------------------------

    [Fact]
    public void SetAttributeValues_stores_comma_separated_sorted_ids()
    {
        var v = ProductVariant.Create(ProductId, null, null, 0);
        var id1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var id2 = Guid.Parse("00000000-0000-0000-0000-000000000002");
        v.SetAttributeValues([id1, id2]);
        v.AttributeValueIds.Should().Be($"{id1},{id2}");
    }

    [Fact]
    public void SetAttributeValues_overwrites_previous_value()
    {
        var v = ProductVariant.Create(ProductId, null, null, 0);
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        v.SetAttributeValues([id1]);
        v.SetAttributeValues([id2]);
        v.AttributeValueIds.Should().Contain(id2.ToString());
        v.AttributeValueIds.Should().NotContain(id1.ToString());
    }
}
