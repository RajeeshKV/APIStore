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

    bool IsFeatured,

    /// <summary>
    /// Mean rating across published reviews, to 2 decimal places.
    /// </summary>
    /// <remarks>
    /// Denormalised onto the product row and maintained by ProductReviewRatingRecalculator, so this
    /// costs no aggregate query per product. <b>Read <see cref="RatingCount"/> first</b>: when it is
    /// zero the product has no published reviews and this value is 0, which must be rendered as
    /// "no ratings yet" rather than as a score of zero.
    /// </remarks>
    decimal RatingAverage,

    /// <summary>Number of published reviews. Zero means the product has not been rated yet.</summary>
    int RatingCount)
{
    /// <summary>
    /// True when at least one published review exists. Prefer this over testing
    /// <see cref="RatingCount"/> or <see cref="RatingAverage"/> for readability at call sites.
    /// </summary>
    public bool HasRatings => RatingCount > 0;
}
