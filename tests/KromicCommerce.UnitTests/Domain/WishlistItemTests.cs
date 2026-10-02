using KromicCommerce.Domain.Identity;
using KromicCommerce.Domain.Identity.Events;

namespace KromicCommerce.UnitTests.Domain;

public sealed class WishlistItemTests
{
    private static readonly Guid Customer = Guid.NewGuid();
    private static readonly Guid Product = Guid.NewGuid();

    [Fact]
    public void Create_stores_customer_product_and_variant()
    {
        var variant = Guid.NewGuid();

        var item = WishlistItem.Create(Customer, Product, variant);

        item.CustomerId.Should().Be(Customer);
        item.ProductId.Should().Be(Product);
        item.ProductVariantId.Should().Be(variant);
    }

    [Fact]
    public void Create_defaults_to_a_product_level_entry() =>
        WishlistItem.Create(Customer, Product).ProductVariantId.Should().BeNull();

    [Fact]
    public void Create_treats_empty_variant_id_as_null() =>
        WishlistItem.Create(Customer, Product, Guid.Empty).ProductVariantId.Should().BeNull();

    [Fact]
    public void Create_rejects_empty_customer_id() =>
        Assert.Throws<ArgumentException>(() => WishlistItem.Create(Guid.Empty, Product));

    [Fact]
    public void Create_rejects_empty_product_id() =>
        Assert.Throws<ArgumentException>(() => WishlistItem.Create(Customer, Guid.Empty));

    [Fact]
    public void Create_assigns_a_fresh_identity() =>
        WishlistItem.Create(Customer, Product).Id.Should().NotBe(Guid.Empty);

    [Fact]
    public void Create_raises_added_event()
    {
        var variant = Guid.NewGuid();

        var item = WishlistItem.Create(Customer, Product, variant);

        var raised = item.DomainEvents.OfType<WishlistItemAddedEvent>().Should().ContainSingle().Subject;
        raised.CustomerId.Should().Be(Customer);
        raised.ProductId.Should().Be(Product);
        raised.ProductVariantId.Should().Be(variant);
    }

    [Fact]
    public void RaiseRemoved_raises_removed_event_with_the_same_scope()
    {
        var item = WishlistItem.Create(Customer, Product);
        item.RaiseRemoved();

        var raised = item.DomainEvents.OfType<WishlistItemRemovedEvent>().Should().ContainSingle().Subject;
        raised.WishlistItemId.Should().Be(item.Id);
        raised.CustomerId.Should().Be(Customer);
        raised.ProductId.Should().Be(Product);
    }

    [Fact]
    public void Variant_is_immutable_after_creation()
    {
        // "Switch to a different variant" is expressed as remove-then-add, which keeps the
        // (CustomerId, ProductId, ProductVariantId) unique index trivial.
        var item = WishlistItem.Create(Customer, Product, Guid.NewGuid());

        // Reflection surfaces the private setter via SetMethod, so assert on accessibility
        // rather than on null.
        var setter = typeof(WishlistItem)
            .GetProperty(nameof(WishlistItem.ProductVariantId))!
            .SetMethod;

        setter.Should().NotBeNull("EF needs a setter to map the column");
        setter!.IsPublic.Should().BeFalse("ProductVariantId must not be reassignable by callers");
    }
}