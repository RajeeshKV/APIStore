namespace KromicCommerce.Contracts.Cart;

public sealed record CartResponse(
    Guid CartId,
    IReadOnlyList<CartItemResponse> Items,

    /// <summary>Sum of all line totals — server-calculated.</summary>
    decimal Subtotal,
    string Currency,
    int TotalItems,
    bool IsEmpty,

    /// <summary>
    /// Coupon code currently stored on the cart, normalised (trimmed, upper-cased), or null.
    /// The discount is NOT included in <see cref="Subtotal"/> and is never returned from the
    /// cart endpoint — call the checkout summary endpoint for the full payable breakdown.
    /// </summary>
    string? CouponCode = null);
