namespace KromicCommerce.Domain.Cart;

/// <summary>
/// An item in the shopping cart.
/// Prices are NOT stored here — they are retrieved fresh from the catalog at checkout.
/// Never trust client-provided prices.
/// </summary>
public sealed class CartItem : Entity
{
    private CartItem() { } // EF constructor

    internal static CartItem Create(Guid cartId, Guid productId, Guid? variantId, int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be > 0.", nameof(quantity));

        return new CartItem
        {
            CartId = cartId,
            ProductId = productId,
            VariantId = variantId,
            Quantity = quantity,
            AddedAt = DateTime.UtcNow
        };
    }

    public Guid CartId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid? VariantId { get; private set; }
    public int Quantity { get; private set; }
    public DateTime AddedAt { get; private set; }

    // Navigation
    public Cart Cart { get; private set; } = null!;

    // -----------------------------------------------------------------------
    // Behaviour
    // -----------------------------------------------------------------------

    internal void SetQuantity(int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be > 0.", nameof(quantity));
        Quantity = quantity;
    }
}
