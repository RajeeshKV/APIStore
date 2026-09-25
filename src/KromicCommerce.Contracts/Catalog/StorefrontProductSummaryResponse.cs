namespace KromicCommerce.Contracts.Catalog;

/// <summary>
/// Lightweight product representation for storefront listing pages.
/// Does not include description, attributes, or variants — use StorefrontProductResponse for detail.
/// </summary>
public sealed record StorefrontProductSummaryResponse(
    Guid Id,
    string Name,
    string Slug,
    string? ShortDescription,
    decimal Price,
    decimal? CompareAtPrice,

    /// <summary>ISO 4217 currency code from BusinessSettings (e.g. "INR").</summary>
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

    bool IsFeatured);
