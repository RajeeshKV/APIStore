namespace KromicCommerce.Domain.Catalog;

/// <summary>
/// A customer-authored product review, optionally about one specific variant.
///
/// Persistence shape mirrors <see cref="ProductImage"/>: an Entity with an owned
/// <see cref="MediaAsset"/>, so the Cloudinary metadata flattens into the same column
/// prefixes and needs no new column strategy.
///
/// IsPrimary is deliberately omitted. Gallery order alone is sufficient for review photos.
///
/// Hard delete, consistent with the rest of this schema.
/// </summary>
public sealed class ReviewImage : Entity
{
    private ReviewImage() { } // EF constructor

    public static ReviewImage Create(Guid reviewId, MediaAsset asset, int sortOrder)
        => new()
        {
            ReviewId = reviewId,
            Asset = asset,
            SortOrder = sortOrder,
            CreatedAt = DateTime.UtcNow
        };

    public Guid ReviewId { get; private set; }
    public MediaAsset Asset { get; private set; } = null!;
    public int SortOrder { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // Navigation
    public ProductReview Review { get; private set; } = null!;
}