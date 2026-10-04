using KromicCommerce.Application.Abstractions.Data;
using KromicCommerce.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

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

        product.SetRatingAggregate(
            ReviewRatingAggregate.FromRatings(await PublishedRatingsAsync(productId, ct)));

        return true;
    }

    /// <summary>
    /// The ratings of every review that will be <em>published once this unit of work commits</em>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This has to be assembled from two sources, and getting it wrong is invisible. A LINQ query
    /// goes to PostgreSQL and cannot see the change tracker, so on its own it reports the rows as
    /// they are <em>before</em> the pending change — while the caller is about to save that same
    /// change. Recalculating a review from the database and then saving it in one commit computes
    /// the aggregate from the state that was just invalidated, and writes the wrong number
    /// permanently.
    /// </para>
    /// <para>
    /// The drift is different in each direction, which is why it reads as a cache bug and is not
    /// one. Moderating Pending → Published omits the newly published review, so the count stays
    /// too low; the reverse, and a delete, both keep a row that is about to leave the published
    /// set, so the count stays too high.
    /// </para>
    /// <para>
    /// Starting from the committed rows and folding in the tracked entries makes the read describe
    /// the transaction's outcome rather than its starting point, and it keeps the aggregate in the
    /// same SaveChanges as the reviews it summarises — so the two still land together.
    /// </para>
    /// </remarks>
    private async Task<List<int>> PublishedRatingsAsync(Guid productId, CancellationToken ct)
    {
        var persisted = await db.ProductReviews
            .AsNoTracking()
            .Where(r => r.ProductId == productId)
            .Select(r => new { r.Id, r.Rating, r.Status })
            .ToListAsync(ct);

        var effective = persisted.ToDictionary(
            r => r.Id,
            r => (Rating: r.Rating, r.Status));

        foreach (var entry in db.ChangeTracker.Entries<ProductReview>())
        {
            if (entry.Entity.ProductId != productId) continue;

            switch (entry.State)
            {
                // Added and Modified are the same case: the tracked value is the one that will be
                // written, so it replaces whatever the database still holds.
                case EntityState.Added:
                case EntityState.Modified:
                    effective[entry.Entity.Id] = (entry.Entity.Rating, entry.Entity.Status);
                    break;

                // The row is still in the database and still counted in the query above, so it has
                // to come back out here or the count can never fall.
                case EntityState.Deleted:
                    effective.Remove(entry.Entity.Id);
                    break;
            }
        }

        return effective.Values
            .Where(r => r.Status == ReviewStatus.Published)
            .Select(r => r.Rating)
            .ToList();
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
