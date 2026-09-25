namespace KromicCommerce.Domain.Cart;

/// <summary>
/// Shopping cart aggregate.
/// Supports both authenticated customers (CustomerId set) and anonymous visitors
/// (AnonymousId — a server-issued secure random token, not the customer ID).
/// Cart items do NOT store prices; prices are always retrieved fresh from the catalog
/// at checkout time to prevent price manipulation.
/// Inventory is NOT reserved when adding to cart; reservation happens at checkout.
/// </summary>
public sealed class Cart : AuditableEntity
{
    private Cart() { } // EF constructor

    public static Cart CreateForCustomer(Guid customerId)
        => new()
        {
            CustomerId = customerId,
            AnonymousId = null,
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        };

    public static Cart CreateAnonymous(string anonymousId)
    {
        if (string.IsNullOrWhiteSpace(anonymousId))
            throw new ArgumentException("Anonymous cart ID must not be empty.", nameof(anonymousId));
        return new Cart
        {
            CustomerId = null,
            AnonymousId = anonymousId.Trim(),
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };
    }

    /// <summary>Set when the cart belongs to an authenticated customer.</summary>
    public Guid? CustomerId { get; private set; }

    /// <summary>
    /// Server-issued opaque token for anonymous carts.
    /// Must be random and unpredictable — never derived from the customer ID.
    /// </summary>
    public string? AnonymousId { get; private set; }

    public DateTime ExpiresAt { get; private set; }

    private readonly List<CartItem> _items = [];
    public IReadOnlyList<CartItem> Items => _items.AsReadOnly();

    // -----------------------------------------------------------------------
    // Behaviour
    // -----------------------------------------------------------------------

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;

    public void AssignToCustomer(Guid customerId)
    {
        CustomerId = customerId;
        ExpiresAt = DateTime.UtcNow.AddDays(30); // extend on login
    }

    public void ExtendExpiry(int days) =>
        ExpiresAt = DateTime.UtcNow.AddDays(days);

    /// <summary>
    /// Adds or increments a cart item. The product/variant validity and inventory
    /// check is the caller's responsibility (done in the application handler).
    /// </summary>
    public CartItem AddItem(Guid productId, Guid? variantId, int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be > 0.", nameof(quantity));

        var existing = _items.FirstOrDefault(i =>
            i.ProductId == productId && i.VariantId == variantId);

        if (existing is not null)
        {
            existing.SetQuantity(existing.Quantity + quantity);
            return existing;
        }

        var item = CartItem.Create(Id, productId, variantId, quantity);
        _items.Add(item);
        return item;
    }

    public void UpdateItemQuantity(Guid cartItemId, int quantity)
    {
        var item = _items.FirstOrDefault(i => i.Id == cartItemId)
            ?? throw new InvalidOperationException($"Cart item {cartItemId} not found.");
        item.SetQuantity(quantity);
    }

    public void RemoveItem(Guid cartItemId)
    {
        var item = _items.FirstOrDefault(i => i.Id == cartItemId);
        if (item is not null) _items.Remove(item);
    }

    public void Clear() => _items.Clear();
}
