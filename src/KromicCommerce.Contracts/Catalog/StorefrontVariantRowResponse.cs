namespace KromicCommerce.Contracts.Catalog;

/// <summary>
/// One row in the variant-level product grid. Each active variant of an active product
/// is returned as a separate card, so "Blue-256GB" and "Red-256GB" appear as two distinct
/// rows. Products without variants appear once with VariantId null.
/// </summary>
public sealed record StorefrontVariantRowResponse(
    Guid Id,

    /// <summary>Variant id. Null for products that have no variants — the row represents the product itself.</summary>
    Guid? VariantId,

    Guid ProductId,
    string Slug,
    string Name,
    string? Sku,
    decimal EffectivePrice,

    /// <summary>Compare-at price for the variant (variant override) or product (fallback). Null when not set.</summary>
    decimal? CompareAtPrice,

    string Currency,

    string? PrimaryImageUrl,

    StockAvailability StockAvailability,
    bool CanPurchase,

    Guid? CategoryId,
    string? CategoryName,
    string? CategorySlug,

    Guid? BrandId,
    string? BrandName,
    string? BrandSlug,

    bool IsFeatured,
    decimal RatingAverage,
    int RatingCount,

    /// <summary>
    /// Resolved variant attributes for display (e.g., Color: Red, Storage: 128GB).
    /// Empty when the product has no variants or the variant has no attribute values.
    /// </summary>
    IReadOnlyList<VariantAttributeValueResponse>? VariantAttributes = null,

    /// <summary>
    /// Variant-level images ordered by SortOrder. Empty when none exist; the product-level
    /// image gallery on the PDP is the fallback.
    /// </summary>
    IReadOnlyList<StorefrontImageResponse>? Images = null)
{
    public bool IsOutOfStock => StockAvailability == StockAvailability.OutOfStock;
}
