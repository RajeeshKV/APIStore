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

    /// <summary>
    /// Mean rating across published reviews, to 2 decimal places.
    /// </summary>
    /// <remarks>
    /// Denormalised onto the product row and maintained by ProductReviewRatingRecalculator, so the
    /// product page costs no extra aggregate query. <b>Read <see cref="RatingCount"/> first</b>:
    /// when it is zero the product has no published reviews and this value is 0, which must be
    /// rendered as "no ratings yet" rather than as a score of zero. The same values appear on
    /// every <c>StorefrontProductSummaryResponse</c>, so a listing card and this page never disagree.
    /// </remarks>
    decimal RatingAverage,

    /// <summary>Number of published reviews. Zero means the product has not been rated yet.</summary>
    int RatingCount,

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

    /// <summary>
    /// True when at least one published review exists. Prefer this over testing
    /// <see cref="RatingCount"/> or <see cref="RatingAverage"/> for readability at call sites.
    /// </summary>
    public bool HasRatings => RatingCount > 0;
}

/// <summary>Public image representation — no Cloudinary internal IDs exposed.</summary>
public sealed record StorefrontImageResponse(
    Guid Id,
    string Url,
    string? AltText,
    int SortOrder,
    bool IsPrimary);
