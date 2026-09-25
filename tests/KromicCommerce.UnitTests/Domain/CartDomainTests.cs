using KromicCommerce.Domain.Cart;

namespace KromicCommerce.UnitTests.Domain;

public sealed class CartDomainTests
{
    // -----------------------------------------------------------------------
    // Cart creation
    // -----------------------------------------------------------------------

    [Fact]
    public void CreateForCustomer_sets_30_day_expiry()
    {
        var cart = Cart.CreateForCustomer(Guid.NewGuid());
        cart.CustomerId.Should().NotBeNull();
        cart.AnonymousId.Should().BeNull();
        cart.ExpiresAt.Should().BeAfter(DateTime.UtcNow.AddDays(29));
    }

    [Fact]
    public void CreateAnonymous_sets_7_day_expiry()
    {
        var cart = Cart.CreateAnonymous("secure-token-abc");
        cart.AnonymousId.Should().Be("secure-token-abc");
        cart.CustomerId.Should().BeNull();
        cart.ExpiresAt.Should().BeAfter(DateTime.UtcNow.AddDays(6));
    }

    [Fact]
    public void CreateAnonymous_throws_for_empty_id()
    {
        var act = () => Cart.CreateAnonymous("");
        act.Should().Throw<ArgumentException>();
    }

    // -----------------------------------------------------------------------
    // Add item
    // -----------------------------------------------------------------------

    [Fact]
    public void AddItem_adds_new_item()
    {
        var cart = Cart.CreateForCustomer(Guid.NewGuid());
        var productId = Guid.NewGuid();
        cart.AddItem(productId, null, 2);
        cart.Items.Should().ContainSingle(i => i.ProductId == productId && i.Quantity == 2);
    }

    [Fact]
    public void AddItem_increments_existing_item()
    {
        var cart = Cart.CreateForCustomer(Guid.NewGuid());
        var productId = Guid.NewGuid();
        cart.AddItem(productId, null, 2);
        cart.AddItem(productId, null, 3);
        cart.Items.Should().ContainSingle(i => i.ProductId == productId && i.Quantity == 5);
    }

    [Fact]
    public void AddItem_different_variants_creates_separate_items()
    {
        var cart = Cart.CreateForCustomer(Guid.NewGuid());
        var productId = Guid.NewGuid();
        var v1 = Guid.NewGuid();
        var v2 = Guid.NewGuid();
        cart.AddItem(productId, v1, 1);
        cart.AddItem(productId, v2, 1);
        cart.Items.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AddItem_throws_for_invalid_quantity(int qty)
    {
        var cart = Cart.CreateForCustomer(Guid.NewGuid());
        var act = () => cart.AddItem(Guid.NewGuid(), null, qty);
        act.Should().Throw<ArgumentException>();
    }

    // -----------------------------------------------------------------------
    // Update and remove
    // -----------------------------------------------------------------------

    [Fact]
    public void UpdateItemQuantity_changes_quantity()
    {
        var cart = Cart.CreateForCustomer(Guid.NewGuid());
        cart.AddItem(Guid.NewGuid(), null, 2);
        var itemId = cart.Items[0].Id;
        cart.UpdateItemQuantity(itemId, 5);
        cart.Items[0].Quantity.Should().Be(5);
    }

    [Fact]
    public void UpdateItemQuantity_throws_for_missing_item()
    {
        var cart = Cart.CreateForCustomer(Guid.NewGuid());
        var act = () => cart.UpdateItemQuantity(Guid.NewGuid(), 1);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RemoveItem_removes_correct_item()
    {
        var cart = Cart.CreateForCustomer(Guid.NewGuid());
        cart.AddItem(Guid.NewGuid(), null, 1);
        var itemId = cart.Items[0].Id;
        cart.RemoveItem(itemId);
        cart.Items.Should().BeEmpty();
    }

    [Fact]
    public void RemoveItem_unknown_id_is_noop()
    {
        var cart = Cart.CreateForCustomer(Guid.NewGuid());
        cart.AddItem(Guid.NewGuid(), null, 1);
        cart.RemoveItem(Guid.NewGuid()); // unknown id
        cart.Items.Should().HaveCount(1);
    }

    [Fact]
    public void Clear_removes_all_items()
    {
        var cart = Cart.CreateForCustomer(Guid.NewGuid());
        cart.AddItem(Guid.NewGuid(), null, 1);
        cart.AddItem(Guid.NewGuid(), null, 2);
        cart.Clear();
        cart.Items.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // IsExpired
    // -----------------------------------------------------------------------

    [Fact]
    public void IsExpired_false_for_fresh_cart()
    {
        var cart = Cart.CreateForCustomer(Guid.NewGuid());
        cart.IsExpired.Should().BeFalse();
    }
}
