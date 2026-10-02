namespace KromicCommerce.Domain.Catalog;

/// <summary>
/// One customer's "this helped" vote on one review.
///
/// Existence of the row *is* the vote, so there is no flag column that can drift out of
/// agreement with <see cref="ProductReview.HelpfulCount"/>. A second POST from the same
/// customer deletes the row and decrements. The unique index on (ReviewId, CustomerId)
/// makes the toggle safe under concurrent double-taps.
/// </summary>
public sealed class ReviewHelpfulVote : Entity
{
    private ReviewHelpfulVote() { } // EF constructor

    public static ReviewHelpfulVote Create(Guid reviewId, Guid customerId)
        => new()
        {
            ReviewId = reviewId,
            CustomerId = customerId,
            CreatedAt = DateTime.UtcNow
        };

    public Guid ReviewId { get; private set; }
    public Guid CustomerId { get; private set; }
    public DateTime CreatedAt { get; private set; }
}