namespace KromicCommerce.Contracts.Cart;

public sealed record CartResponse(
    Guid CartId,
    IReadOnlyList<CartItemResponse> Items,

    /// <summary>Sum of all line totals — server-calculated.</summary>
    decimal Subtotal,
    string Currency,
    int TotalItems,
    bool IsEmpty);
