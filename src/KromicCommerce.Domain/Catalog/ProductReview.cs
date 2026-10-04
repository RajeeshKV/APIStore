using KromicCommerce.Domain.Catalog.Events;

namespace KromicCommerce.Domain.Catalog;

/// <summary>
/// A customer-authored rating and comment for a product, optionally scoped to one variant.
///
/// Rules that are enforced here rather than only in a validator:
///
///   - Rating is 1..5 inclusive.
///   - Body is required; Title is optional; both are length-capped.
///   - At most <see cref="MaxImages"/> images.
///   - Submissions are created <see cref="ReviewStatus.Published"/> and are public immediately, so
///     a customer never has to wonder where their review went. An admin can return one to
///     <see cref="ReviewStatus.Pending"/>, reject it with a reason, or delete it; those are the
///     levers, not a publish gate.
///   - Edit changes content only. It never touches Status, PublishedAtUtc, IsVerifiedPurchase,
///     or ownership. A published review that a customer edits stays published — resetting it to
///     Pending on every edit is a real product decision, and silently re-reviewing edited
///     content is a well-known source of "my review disappeared" complaints.
///   - IsVerifiedPurchase is derived from delivered orders by the handler. It is never accepted
///     from a request body, because that flag is the only meaningful guard against review spam.
///
/// Uniqueness of (CustomerId, ProductId, ProductVariantId) is enforced by a database unique
/// index created with NULLS NOT DISTINCT, so concurrent submits cannot both win.
/// </summary>
public sealed class ProductReview : AuditableEntity
{
    /// <summary>Maximum photos attached to a single review.</summary>
    public const int MaxImages = 3;

    /// <summary>Maximum length of the optional review title.</summary>
    public const int TitleMaxLength = 120;

    /// <summary>Maximum length of the review body.</summary>
    public const int BodyMaxLength = 4000;

    /// <summary>Maximum length of an admin's rejection reason.</summary>
    public const int ModerationReasonMaxLength = 500;

    private readonly List<ReviewImage> _images = [];

    private ProductReview() { } // EF constructor

    public static ProductReview Create(
        Guid customerId,
        Guid productId,
        Guid? productVariantId,
        int rating,
        string? title,
        string body,
        bool isVerifiedPurchase,
        ReviewStatus status)
    {
        if (customerId == Guid.Empty)
            throw new ArgumentException("Customer id is required.", nameof(customerId));
        if (productId == Guid.Empty)
            throw new ArgumentException("Product id is required.", nameof(productId));

        ValidateRating(rating);
        var (cleanTitle, cleanBody) = ValidateContent(title, body);

        var review = new ProductReview
        {
            CustomerId = customerId,
            ProductId = productId,
            ProductVariantId = productVariantId == Guid.Empty ? null : productVariantId,
            Rating = rating,
            Title = cleanTitle,
            Body = cleanBody,
            IsVerifiedPurchase = isVerifiedPurchase,
            Status = status,
            PublishedAtUtc = status == ReviewStatus.Published ? DateTime.UtcNow : null,
            HelpfulCount = 0
        };
        review.RaiseDomainEvent(new ProductReviewSubmittedEvent(
            review.Id, review.CustomerId, review.ProductId, review.Status));
        return review;
    }

    // -----------------------------------------------------------------------
    // Fields
    // -----------------------------------------------------------------------

    public Guid CustomerId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid? ProductVariantId { get; private set; }

    /// <summary>Star rating, 1..5.</summary>
    public int Rating { get; private set; }

    public string? Title { get; private set; }
    public string Body { get; private set; } = string.Empty;

    /// <summary>Derived from delivered orders server-side. Never bound from a request.</summary>
    public bool IsVerifiedPurchase { get; private set; }

    public ReviewStatus Status { get; private set; }
    public string? ModerationReason { get; private set; }
    public DateTime? PublishedAtUtc { get; private set; }

    /// <summary>Denormalised count of <see cref="ReviewHelpfulVote"/> rows. Recalculated, never incremented.</summary>
    public int HelpfulCount { get; private set; }

    public IReadOnlyList<ReviewImage> Images => _images.AsReadOnly();

    // Navigation
    public Product Product { get; private set; } = null!;

    // -----------------------------------------------------------------------
    // Behaviour
    // -----------------------------------------------------------------------

    /// <summary>
    /// Replaces the customer-authored content. Deliberately leaves Status, PublishedAtUtc,
    /// IsVerifiedPurchase, ProductId and CustomerId untouched.
    /// </summary>
    public void Edit(int rating, string? title, string body)
    {
        ValidateRating(rating);
        var (cleanTitle, cleanBody) = ValidateContent(title, body);

        Rating = rating;
        Title = cleanTitle;
        Body = cleanBody;

        RaiseDomainEvent(new ProductReviewEditedEvent(Id, CustomerId, ProductId));
    }

    public void AddImage(MediaAsset asset, int sortOrder)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (_images.Count >= MaxImages)
            throw new ArgumentException($"A review may have at most {MaxImages} images.", nameof(asset));

        _images.Add(ReviewImage.Create(Id, asset, sortOrder));
    }

    public void RemoveImage(Guid imageId)
    {
        var image = _images.FirstOrDefault(i => i.Id == imageId)
            ?? throw new ArgumentException("Image does not belong to this review.", nameof(imageId));
        _images.Remove(image);
    }

    /// <summary>
    /// The only place Status changes. Stamps PublishedAtUtc on the first transition into
    /// Published and clears it when leaving. Same-status transitions are no-ops.
    /// </summary>
    public void SetStatus(ReviewStatus next, string? moderationReason = null)
    {
        if (Status == next && next != ReviewStatus.Rejected)
            return;

        var previous = Status;
        Status = next;

        if (next == ReviewStatus.Published)
        {
            // Stamped on every entry into Published, not only the first. Leaving Published
            // clears PublishedAtUtc (so the public listing filter stays honest), which means
            // there is nothing left to preserve here — a review that is moderated out and later
            // re-approved becomes publicly visible again at that moment, and sorting by
            // recency should reflect that rather than its original submission date.
            PublishedAtUtc = DateTime.UtcNow;
            ModerationReason = null;
        }
        else
        {
            PublishedAtUtc = null;
            ModerationReason = next == ReviewStatus.Rejected
                ? Truncate(moderationReason, ModerationReasonMaxLength)
                : null;
        }

        RaiseDomainEvent(new ProductReviewStatusChangedEvent(Id, ProductId, previous, next));
    }

    /// <summary>Called by the handler from a delivered-order lookup. Never from a request body.</summary>
    public void SetVerifiedPurchase(bool value) => IsVerifiedPurchase = value;

    public void SetHelpfulCount(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Helpful count cannot be negative.");
        HelpfulCount = count;
    }

    /// <summary>
    /// Raised by the handler immediately before the row is removed — a deleted row cannot
    /// raise events on its own. Carries ProductId so the rating aggregate can be recalculated
    /// off the event rather than off handler bookkeeping.
    /// </summary>
    public void RaiseDeleted() =>
        RaiseDomainEvent(new ProductReviewDeletedEvent(Id, CustomerId, ProductId, ProductVariantId));

    public bool IsOwnedBy(Guid customerId) => CustomerId == customerId;

    // -----------------------------------------------------------------------
    // Guard
    // -----------------------------------------------------------------------

    private static void ValidateRating(int rating)
    {
        if (rating is < 1 or > 5)
            throw new ArgumentOutOfRangeException(
                nameof(rating), rating, "Rating must be between 1 and 5.");
    }

    private static (string? Title, string Body) ValidateContent(string? title, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            throw new ArgumentException("Review body is required.", nameof(body));

        var cleanBody = body.Trim();
        if (cleanBody.Length > BodyMaxLength)
            throw new ArgumentException(
                $"Review body must be {BodyMaxLength} characters or fewer.", nameof(body));

        var cleanTitle = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        if (cleanTitle is { Length: > TitleMaxLength })
            throw new ArgumentException(
                $"Review title must be {TitleMaxLength} characters or fewer.", nameof(title));

        return (cleanTitle, cleanBody);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}