using KromicCommerce.Contracts.Catalog;

namespace KromicCommerce.Contracts.Orders;

public sealed record OrderItemResponse(
    Guid Id,
    Guid ProductId,
    Guid? VariantId,
    string ProductName,
    string? VariantDescription,
    string? Sku,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal,
    /// <summary>Primary product image URL at the time of the order. Null if no image exists.</summary>
    string? PrimaryImageUrl,

    /// <summary>
    /// Resolved variant attributes for display (e.g., Color: Orange, Storage: 256GB).
    /// Empty when the product has no variants or the variant has no attribute values.
    /// </summary>
    IReadOnlyList<VariantAttributeValueResponse>? VariantAttributes = null);
