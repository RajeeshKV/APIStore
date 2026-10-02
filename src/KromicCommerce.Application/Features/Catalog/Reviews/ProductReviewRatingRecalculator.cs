namespace KromicCommerce.Application.Features.Catalog.Reviews;

/// <summary>
/// Recalculates a product's denormalised rating summary from the full set of published reviews.
///
/// Every path that can change the published set — submit, edit, moderate, delete, product
/// cascade-delete — must call this. It recomputes from scratch rather than incrementing a
/// running total: an incremental counter drifts as soon as a second write path exists, and the
/// drift is silent because the number still looks plausible.
///
/// Must be invoked before the single SaveChangesAsync of its handler, so the product row and the
/// review rows land together or not at all.
/// </summary>
internal sealed class ProductReviewRatingRecalculator(IApplicationDbContext db)
{
    /// <summary>
    /// Loads the product, recomputes from published reviews, and applies the result.
    /// Returns false when the product no longer exists (e.g. it was deleted and its reviews
    /// cascaded away), which is not an error — there is simply nothing left to update.
    /// </summary>
    public async Task<bool> RecalculateAsync(Guid productId, CancellationToken ct)
    {
        var product = await db.Products
            .FirstOrDefaultAsync(p => p.Id == productId, ct);

        if (product is null) return false;

        var publishedRatings = await db.ProductReviews
            .AsNoTracking()
            .Where(r => r.ProductId == productId && r.Status == ReviewStatus.Published)
            .Select(r => r.Rating)
            .ToListAsync(ct);

        product.SetRatingAggregate(ReviewRatingAggregate.FromRatings(publishedRatings));
        return true;
    }

    /// <summary>
    /// Recalculates a product's helpful counts for the given review ids.
    ///
    /// Helpful votes are counted from the vote table rather than incremented, for the same
    /// reason the rating is: the count must be able to return to zero, which an increment cannot
    /// do when the last vote is withdrawn.
    /// </summary>
    public async Task RecalculateHelpfulCountsAsync(
        IEnumerable<Guid> reviewIds, CancellationToken ct)
    {
        var ids = reviewIds.Distinct().ToList();
        if (ids.Count == 0) return;

        var counts = await db.ReviewHelpfulVotes
            .AsNoTracking()
            .Where(v => ids.Contains(v.ReviewId))
            .GroupBy(v => v.ReviewId)
            .Select(g => new { ReviewId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ReviewId, x => x.Count, ct);

        var reviews = await db.ProductReviews
            .Where(r => ids.Contains(r.Id))
            .ToListAsync(ct);

        foreach (var review in reviews)
            review.SetHelpfulCount(counts.GetValueOrDefault(review.Id));
    }
}