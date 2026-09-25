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
    string? MetaKeywords);

/// <summary>Public image representation — no Cloudinary internal IDs exposed.</summary>
public sealed record StorefrontImageResponse(
    Guid Id,
    string Url,
    string? AltText,
    int SortOrder,
    bool IsPrimary);
