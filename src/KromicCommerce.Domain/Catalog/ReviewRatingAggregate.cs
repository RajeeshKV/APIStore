namespace KromicCommerce.Domain.Catalog;

/// <summary>
/// The published-review rating summary stored on <see cref="Product"/>.
///
/// Denormalised on purpose. The product-detail response is cached, so a rating aggregate that
/// were computed on read would either be served stale after review activity or force cache
/// invalidation on every read as well as every write. Storing it makes cache correctness
/// identical to the rest of the product response.
///
/// ALWAYS recalculate from the published set — never increment a running total. An
/// incremental counter drifts the moment a second write path is added, and the drift is
/// silent: the number looks plausible while being wrong forever.
/// </summary>
public sealed record ReviewRatingAggregate(decimal Average, int Count)
{
    /// <summary>The value for a product with no published reviews.</summary>
    public static readonly ReviewRatingAggregate Empty = new(0m, 0);

    /// <summary>
    /// Computes the aggregate from every published review rating.
    /// Returns <see cref="Empty"/> for an empty set — never null, and never the last known
    /// value, so removing the final published review correctly returns the product to zero.
    /// </summary>
    public static ReviewRatingAggregate FromRatings(IEnumerable<int> publishedRatings)
    {
        ArgumentNullException.ThrowIfNull(publishedRatings);

        var sum = 0;
        var count = 0;
        foreach (var rating in publishedRatings)
        {
            sum += rating;
            count++;
        }

        if (count == 0) return Empty;

        // Midpoint rounding, then clamped to the column's precision (numeric(3,2)).
        var average = Math.Round((decimal)sum / count, 2, MidpointRounding.AwayFromZero);
        return new ReviewRatingAggregate(average, count);
    }
}