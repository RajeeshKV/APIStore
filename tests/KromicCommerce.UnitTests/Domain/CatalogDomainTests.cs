using KromicCommerce.Domain.Catalog;

namespace KromicCommerce.UnitTests.Domain;

public sealed class CatalogDomainTests
{
    // -----------------------------------------------------------------------
    // Category
    // -----------------------------------------------------------------------

    [Fact]
    public void Category_Create_lowercases_slug()
    {
        var cat = Category.Create("Electronics", "ELECTRONICS", null, null);
        cat.Slug.Should().Be("electronics");
    }

    [Fact]
    public void Category_Create_raises_CategoryCreatedEvent()
    {
        var cat = Category.Create("A", "a", null, null);
        cat.DomainEvents.Should().ContainSingle(e => e is CategoryCreatedEvent);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Category_Create_throws_for_empty_name(string name)
    {
        var act = () => Category.Create(name, "slug", null, null);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Category_Deactivate_sets_IsActive_false()
    {
        var cat = Category.Create("X", "x", null, null);
        cat.Deactivate();
        cat.IsActive.Should().BeFalse();
        cat.Activate();
        cat.IsActive.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Brand
    // -----------------------------------------------------------------------

    [Fact]
    public void Brand_Create_lowercases_slug()
    {
        var b = Brand.Create("Nike", "NIKE", null, null);
        b.Slug.Should().Be("nike");
    }

    [Fact]
    public void Brand_Create_throws_for_empty_slug()
    {
        var act = () => Brand.Create("Nike", "", null, null);
        act.Should().Throw<ArgumentException>();
    }

    // -----------------------------------------------------------------------
    // Product pricing
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(0.01)]
    [InlineData(99.99)]
    public void Product_Create_succeeds_with_valid_price(decimal price)
    {
        var p = Product.Create("Widget", "widget", null, price, null, null);
        p.Price.Should().Be(price);
        p.Status.Should().Be(ProductStatus.Draft);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Product_Create_throws_for_negative_price(decimal price)
    {
        var act = () => Product.Create("Widget", "widget", null, price, null, null);
        act.Should().Throw<ArgumentException>().WithMessage("*>= 0*");
    }

    [Fact]
    public void Product_Create_zero_price_is_valid()
    {
        var p = Product.Create("Free Item", "free-item", null, 0m, null, null);
        p.Price.Should().Be(0m);
    }

    [Fact]
    public void Product_UpdatePricing_throws_when_compareAtPrice_equal_to_price()
    {
        var p = Product.Create("X", "x", null, 100m, null, null);
        var act = () => p.UpdatePricing(100m, 100m); // equal — must be strictly greater
        act.Should().Throw<ArgumentException>().WithMessage("*Compare-at*");
    }

    [Fact]
    public void Product_UpdatePricing_throws_when_compareAtPrice_less_than_price()
    {
        var p = Product.Create("X", "x", null, 100m, null, null);
        var act = () => p.UpdatePricing(100m, 50m);
        act.Should().Throw<ArgumentException>().WithMessage("*Compare-at*");
    }

    [Fact]
    public void Product_UpdatePricing_succeeds_when_compareAtPrice_greater_than_price()
    {
        var p = Product.Create("X", "x", null, 100m, null, null);
        p.UpdatePricing(100m, 150m);
        p.CompareAtPrice.Should().Be(150m);
    }

    [Fact]
    public void Product_UpdatePricing_null_compareAtPrice_is_valid()
    {
        var p = Product.Create("X", "x", null, 100m, null, null);
        p.UpdatePricing(100m, null);
        p.CompareAtPrice.Should().BeNull();
    }

    [Fact]
    public void Product_UpdatePricing_zero_price_with_compareAtPrice_is_valid()
    {
        var p = Product.Create("Free", "free", null, 0m, null, null);
        p.UpdatePricing(0m, 100m);
        p.Price.Should().Be(0m);
        p.CompareAtPrice.Should().Be(100m);
    }

    // -----------------------------------------------------------------------
    // GetEffectivePrice — single source of truth
    // -----------------------------------------------------------------------

    [Fact]
    public void GetEffectivePrice_no_variant_returns_product_price()
    {
        var p = Product.Create("X", "x", null, 200m, null, null);
        p.GetEffectivePrice(null).Should().Be(200m);
    }

    [Fact]
    public void GetEffectivePrice_variant_without_override_returns_product_price()
    {
        var productId = Guid.NewGuid();
        var p = Product.Create("X", "x", null, 200m, null, null);
        var variant = ProductVariant.Create(productId, null, null); // no override
        p.GetEffectivePrice(variant).Should().Be(200m);
    }

    [Fact]
    public void GetEffectivePrice_variant_with_override_returns_override()
    {
        var productId = Guid.NewGuid();
        var p = Product.Create("X", "x", null, 200m, null, null);
        var variant = ProductVariant.Create(productId, null, 150m);
        p.GetEffectivePrice(variant).Should().Be(150m);
    }

    [Fact]
    public void GetEffectivePrice_variant_with_zero_override_returns_zero()
    {
        var productId = Guid.NewGuid();
        var p = Product.Create("X", "x", null, 200m, null, null);
        var variant = ProductVariant.Create(productId, null, 0m);
        p.GetEffectivePrice(variant).Should().Be(0m);
    }

    [Fact]
    public void GetEffectivePrice_zero_base_price_no_override()
    {
        var p = Product.Create("Free", "free", null, 0m, null, null);
        p.GetEffectivePrice(null).Should().Be(0m);
    }

    [Fact]
    public void GetEffectivePrice_multiple_variants_each_returns_own_price()
    {
        var productId = Guid.NewGuid();
        var p = Product.Create("X", "x", null, 100m, null, null);
        var v1 = ProductVariant.Create(productId, "SKU-A", 80m);
        var v2 = ProductVariant.Create(productId, "SKU-B", 120m);
        var v3 = ProductVariant.Create(productId, "SKU-C", null); // inherits product price

        p.GetEffectivePrice(v1).Should().Be(80m);
        p.GetEffectivePrice(v2).Should().Be(120m);
        p.GetEffectivePrice(v3).Should().Be(100m);
    }

    // -----------------------------------------------------------------------
    // ProductVariant price override
    // -----------------------------------------------------------------------

    [Fact]
    public void Variant_Create_accepts_zero_price_override()
    {
        var v = ProductVariant.Create(Guid.NewGuid(), null, 0m);
        v.PriceOverride.Should().Be(0m);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(-1)]
    public void Variant_Create_throws_for_negative_price_override(decimal price)
    {
        var act = () => ProductVariant.Create(Guid.NewGuid(), null, price);
        act.Should().Throw<ArgumentException>().WithMessage("*>= 0*");
    }

    [Fact]
    public void Variant_Update_accepts_zero_price_override()
    {
        var v = ProductVariant.Create(Guid.NewGuid(), null, 100m);
        v.Update(null, 0m, 0);
        v.PriceOverride.Should().Be(0m);
    }

    // -----------------------------------------------------------------------
    // Product status transitions
    // -----------------------------------------------------------------------

    [Fact]
    public void Product_Publish_transitions_Draft_to_Active()
    {
        var p = Product.Create("X", "x", null, 10m, null, null);
        p.Status.Should().Be(ProductStatus.Draft);
        p.Publish();
        p.Status.Should().Be(ProductStatus.Active);
    }

    [Fact]
    public void Product_Publish_raises_ProductStatusChangedEvent()
    {
        var p = Product.Create("X", "x", null, 10m, null, null);
        p.ClearDomainEvents();
        p.Publish();
        p.DomainEvents.OfType<ProductStatusChangedEvent>()
            .Should().ContainSingle(e => e.NewStatus == ProductStatus.Active);
    }

    [Fact]
    public void Product_Archive_transitions_Active_to_Archived()
    {
        var p = Product.Create("X", "x", null, 10m, null, null);
        p.Publish();
        p.ClearDomainEvents();
        p.Archive();
        p.Status.Should().Be(ProductStatus.Archived);
    }

    [Fact]
    public void Product_Unpublish_sets_status_to_Draft()
    {
        var p = Product.Create("X", "x", null, 10m, null, null);
        p.Publish();
        p.Unpublish();
        p.Status.Should().Be(ProductStatus.Draft);
    }

    [Fact]
    public void Product_Publish_is_idempotent()
    {
        var p = Product.Create("X", "x", null, 10m, null, null);
        p.Publish();
        p.ClearDomainEvents();
        p.Publish(); // second call — should not raise event
        p.DomainEvents.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // InventoryItem
    // -----------------------------------------------------------------------

    [Fact]
    public void InventoryItem_Available_equals_OnHand_minus_Reserved()
    {
        var inv = InventoryItem.Create(Guid.NewGuid(), null, 100, 5);
        inv.Reserve(30);
        inv.Available.Should().Be(70);
    }

    [Fact]
    public void InventoryItem_Reserve_throws_when_exceeds_available()
    {
        var inv = InventoryItem.Create(Guid.NewGuid(), null, 10, 2);
        var act = () => inv.Reserve(15);
        act.Should().Throw<InvalidOperationException>().WithMessage("*15*");
    }

    [Fact]
    public void InventoryItem_Release_decrements_Reserved()
    {
        var inv = InventoryItem.Create(Guid.NewGuid(), null, 50, 5);
        inv.Reserve(20);
        inv.Release(10);
        inv.Reserved.Should().Be(10);
    }

    [Fact]
    public void InventoryItem_FinalizeReservation_reduces_both_Reserved_and_OnHand()
    {
        var inv = InventoryItem.Create(Guid.NewGuid(), null, 50, 5);
        inv.Reserve(10);
        inv.FinalizeReservation(10);
        inv.OnHand.Should().Be(40);
        inv.Reserved.Should().Be(0);
    }

    [Fact]
    public void InventoryItem_SetOnHand_throws_when_below_reserved()
    {
        var inv = InventoryItem.Create(Guid.NewGuid(), null, 50, 5);
        inv.Reserve(20);
        var act = () => inv.SetOnHand(10); // 10 < 20 reserved
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void InventoryItem_AdjustOnHand_negative_throws_when_would_go_negative()
    {
        var inv = InventoryItem.Create(Guid.NewGuid(), null, 5, 1);
        var act = () => inv.AdjustOnHand(-10);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void InventoryItem_IsLowStock_true_when_available_at_or_below_threshold()
    {
        var inv = InventoryItem.Create(Guid.NewGuid(), null, 5, 5);
        inv.IsLowStock.Should().BeTrue();
    }

    [Fact]
    public void InventoryItem_IsOutOfStock_true_when_available_zero()
    {
        var inv = InventoryItem.Create(Guid.NewGuid(), null, 0, 5);
        inv.IsOutOfStock.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // MediaAsset value object
    // -----------------------------------------------------------------------

    [Fact]
    public void MediaAsset_equality_by_value()
    {
        var a = MediaAsset.Create("pub_1", "https://cdn.example.com/1.jpg", "jpg", 800, 600, "alt");
        var b = MediaAsset.Create("pub_1", "https://cdn.example.com/1.jpg", "jpg", 800, 600, "alt");
        a.Should().Be(b);
    }

    [Fact]
    public void MediaAsset_Create_throws_for_empty_publicId()
    {
        var act = () => MediaAsset.Create("", "https://cdn.example.com/1.jpg", null, null, null, null);
        act.Should().Throw<ArgumentException>();
    }
}
