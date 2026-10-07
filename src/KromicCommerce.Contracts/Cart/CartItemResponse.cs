using KromicCommerce.Contracts.Catalog;

namespace KromicCommerce.Contracts.Cart;

public sealed record CartItemResponse(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string ProductSlug,
    Guid? VariantId,
    string? VariantDescription,
    string? Sku,

    /// <summary>Current effective price from catalog — server-calculated, never from frontend.</summary>
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal,
    string Currency,
    StockAvailability StockAvailability,
    bool CanPurchase,
    string? PrimaryImageUrl,

    /// <summary>
    /// Resolved variant attributes for display (e.g., Color: Orange, Storage: 256GB).
    /// Empty when the product has no variants or the variant has no attribute values.
    /// </summary>
    IReadOnlyList<VariantAttributeValueResponse>? VariantAttributes = null);
