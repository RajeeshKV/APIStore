namespace KromicCommerce.Contracts.Me;

/// <summary>
/// Add a product to the wishlist. <see cref="ProductVariantId"/> is optional: omitting it
/// saves the product itself, supplying it pins the entry to one variant.
///
/// There is deliberately no CustomerId field. Ownership always comes from the authenticated
/// caller, never from the request body.
/// </summary>
public sealed record AddWishlistItemRequest(Guid ProductId, Guid? ProductVariantId = null);

/// <summary>
/// Result of an add. <see cref="Created"/> is false when the entry was already on the list,
/// which makes the add idempotent — a double-tapped heart is a UI accident, not a conflict.
/// </summary>
public sealed record AddWishlistItemResponse(WishlistItemResponse Item, bool Created);

/// <summary>
/// Which of the requested products are on the caller's wishlist. Lets a product grid render
/// filled/empty hearts for many products in one request instead of one call per card.
/// </summary>
public sealed record WishlistStatusResponse(IReadOnlyList<Guid> ProductIds);