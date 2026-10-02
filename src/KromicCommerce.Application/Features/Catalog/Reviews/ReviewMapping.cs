using KromicCommerce.Application.Caching;

namespace KromicCommerce.Application.Features.Catalog.Reviews;

internal static class ReviewMapper
{
    /// <summary>
    /// Product identity needed both to build a response and to invalidate the product cache
    /// (which is keyed by slug).
    /// </summary>
    internal static async Task<(string Name, string? Slug)> LoadProductAsync(
        IApplicationDbContext db, Guid productId, CancellationToken ct)
    {
        var product = await db.Products
            .AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => new { p.Name, p.Slug })
            .FirstOrDefaultAsync(ct);

        return product is null ? (string.Empty, null) : (product.Name, product.Slug);
    }
    /// <summary>
    /// Display name for the review author. Falls back to the local part of the email when the
    /// account has no name, so a row is never rendered blank.
    ///
    /// Takes the three fields rather than a User, because the listing query projects only those
    /// columns and materialising a whole tracked entity per author would be wasteful.
    /// </summary>
    internal static string AuthorName(string? firstName, string? lastName, string? email)
    {
        var full = $"{firstName} {lastName}".Trim();
        if (!string.IsNullOrWhiteSpace(full)) return full;

        var mail = email ?? string.Empty;
        var at = mail.IndexOf('@', StringComparison.Ordinal);
        return at > 0 ? mail[..at] : "Customer";
    }

    internal static ReviewImageResponse MapImage(ReviewImage i) =>
        new(i.Id, i.Asset.PublicId, i.Asset.SecureUrl, i.SortOrder);

    internal static IReadOnlyList<ReviewImageResponse> MapImages(IEnumerable<ReviewImage>? images) =>
        images is null
            ? []
            : images.OrderBy(i => i.SortOrder).Select(MapImage).ToList();

    internal static ProductReviewSummaryResponse MapPublic(
        ProductReview r, string authorName) =>
        new(r.Id, r.ProductId, r.ProductVariantId, authorName, r.Rating, r.Title, r.Body,
            r.IsVerifiedPurchase, r.HelpfulCount, MapImages(r.Images),
            r.PublishedAtUtc, r.CreatedAtUtc);

    internal static MyReviewResponse MapMine(ProductReview r, string productName) =>
        new(r.Id, r.ProductId, r.ProductVariantId, productName, r.Rating, r.Title, r.Body,
            r.IsVerifiedPurchase, r.Status.ToString(), r.ModerationReason, r.HelpfulCount,
            MapImages(r.Images), r.PublishedAtUtc, r.CreatedAtUtc, r.UpdatedAtUtc);

    internal static AdminReviewResponse MapAdmin(
        ProductReview r, string productName, string customerEmail) =>
        new(r.Id, r.CustomerId, customerEmail, r.ProductId, productName, r.ProductVariantId,
            r.Rating, r.Title, r.Body, r.IsVerifiedPurchase, r.Status.ToString(),
            r.ModerationReason, r.HelpfulCount, MapImages(r.Images),
            r.PublishedAtUtc, r.CreatedAtUtc, r.UpdatedAtUtc);
}

/// <summary>
/// Shared by the write handlers: verifies the product is live and, when a variant was
/// supplied, that the variant belongs to it and is itself available.
/// </summary>
internal sealed record ReviewTarget(Product Product, ProductVariant? Variant);

internal static class ReviewTargetResolver
{
    internal static async Task<Result<ReviewTarget>> ResolveAsync(
        IApplicationDbContext db, Guid productId, Guid? variantId, CancellationToken ct)
    {
        var product = await db.Products
            .AsNoTracking()
            .Include(p => p.Variants)
            .FirstOrDefaultAsync(p => p.Id == productId, ct);

        if (product is null)
            return Result.Failure<ReviewTarget>(Error.NotFound("PRODUCT_NOT_FOUND", "Product not found."));

        if (product.Status != ProductStatus.Active)
            return Result.Failure<ReviewTarget>(
                Error.Validation("PRODUCT_NOT_AVAILABLE", "This product is not available."));

        if (!variantId.HasValue)
            return new ReviewTarget(product, null);

        var variant = product.Variants.FirstOrDefault(v => v.Id == variantId.Value);
        if (variant is null)
            return Result.Failure<ReviewTarget>(Error.Validation("PRODUCT_VARIANT_MISMATCH",
                "The selected variant does not belong to this product."));

        if (!variant.IsActive)
            return Result.Failure<ReviewTarget>(
                Error.Validation("PRODUCT_VARIANT_UNAVAILABLE", "The selected variant is not available."));

        return new ReviewTarget(product, variant);
    }

    /// <summary>
    /// Derived from delivered orders only — never from the request.
    ///
    /// A self-declared "verified purchase" badge would be worthless, so the DTO has no such
    /// field and the only way to earn it is a real Delivered order for this customer containing
    /// this product.
    /// </summary>
    internal static async Task<bool> IsVerifiedPurchaseAsync(
        IApplicationDbContext db, Guid customerId, Guid productId, CancellationToken ct) =>
        await db.Orders
            .AsNoTracking()
            .AnyAsync(o =>
                o.CustomerId == customerId &&
                o.Status == OrderStatus.Delivered &&
                o.Items.Any(i => i.ProductId == productId), ct);
}

/// <summary>
/// Materialises client-supplied image descriptors into MediaAssets.
///
/// The URL is checked against the Cloudinary host rather than trusted. A tampered client could
/// otherwise persist an arbitrary URL into review content that every other customer renders.
/// </summary>
internal static class ReviewImageFactory
{
    private const string AllowedUrlPrefix = "https://res.cloudinary.com/";

    internal static Result<List<MediaAsset>> Build(IReadOnlyList<ReviewImageRequest>? images)
    {
        var assets = new List<MediaAsset>();
        if (images is null) return assets;

        foreach (var image in images)
        {
            if (string.IsNullOrWhiteSpace(image.PublicId) || string.IsNullOrWhiteSpace(image.Url))
                return Result.Failure<List<MediaAsset>>(Error.Validation("REVIEW_IMAGE_INVALID",
                    "Each review image requires both a public id and a url."));

            if (!image.Url.StartsWith(AllowedUrlPrefix, StringComparison.OrdinalIgnoreCase))
                return Result.Failure<List<MediaAsset>>(Error.Validation("REVIEW_IMAGE_INVALID",
                    "Review images must be hosted on Cloudinary."));

            try
            {
                assets.Add(MediaAsset.Create(image.PublicId, image.Url, null, null, null, null));
            }
            catch (ArgumentException)
            {
                return Result.Failure<List<MediaAsset>>(
                    Error.Validation("REVIEW_IMAGE_INVALID", "Review image metadata is invalid."));
            }
        }

        return assets;
    }
}