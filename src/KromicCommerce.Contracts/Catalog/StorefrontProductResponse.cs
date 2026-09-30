namespace KromicCommerce.Contracts.Catalog;

/// <summary>
/// Full product detail for the storefront product page.
/// Only exposes customer-relevant fields — no internal audit fields, no OnHand/Reserved.
/// </summary>
public sealed record StorefrontProductResponse(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string? ShortDescription,
    decimal Price,
    decimal? CompareAtPrice,

    /// <summary>ISO 4217 currency code from BusinessSettings (e.g. "INR").</summary>
    string Currency,

    StockAvailability StockAvailability,
    bool CanPurchase,

    Guid? CategoryId,
    string? CategoryName,
    string? CategorySlug,

    Guid? BrandId,
    string? BrandName,
    string? BrandSlug,

    bool IsFeatured,

    /// <summary>Images ordered by SortOrder. Primary image is clearly flagged.</summary>
    IReadOnlyList<StorefrontImageResponse> Images,

    IReadOnlyList<ProductAttributeDto> Attributes,
    IReadOnlyList<StorefrontVariantResponse> Variants,

    DeliveryEstimateDto? DeliveryEstimate,

    // SEO metadata
    string? MetaTitle,
    string? MetaDescription,
    string? MetaKeywords)
{
    /// <summary>
    /// DERIVED from <see cref="StockAvailability"/> — never stored or set independently, so the
    /// contradictory "StockAvailability InStock / IsOutOfStock true" state cannot exist.
    ///
    /// For a product with variants this reflects the ROLLUP: true only when every sellable
    /// variant is out of stock. One purchasable variant makes the product NOT out of stock.
    /// It is a convenience projection for clients that want a plain boolean; it carries no
    /// information the enum does not already have.
    /// </summary>
    public bool IsOutOfStock => StockAvailability == StockAvailability.OutOfStock;
}

/// <summary>Public image representation — no Cloudinary internal IDs exposed.</summary>
public sealed record StorefrontImageResponse(
    Guid Id,
    string Url,
    string? AltText,
    int SortOrder,
    bool IsPrimary);
