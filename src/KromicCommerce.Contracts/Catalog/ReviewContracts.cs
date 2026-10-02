using KromicCommerce.Contracts.Common;

namespace KromicCommerce.Contracts.Catalog;

/// <summary>
/// One review photo. <see cref="PublicId"/> is required so the asset can be located on
/// Cloudinary; <see cref="Url"/> must be the HTTPS URL the upload endpoint returned.
/// </summary>
public sealed record ReviewImageRequest(string PublicId, string Url);

/// <summary>
/// Create a product review.
///
/// There is deliberately no <c>isVerifiedPurchase</c> field. That flag is derived server-side
/// from delivered orders; accepting it from the client would make it worthless.
/// There is also no customer id — ownership always comes from the authenticated caller.
/// </summary>
public sealed record CreateReviewRequest(
    Guid? ProductVariantId,
    int Rating,
    string? Title,
    string Body,
    IReadOnlyList<ReviewImageRequest>? Images = null);

public sealed record UpdateReviewRequest(
    int Rating,
    string? Title,
    string Body,
    IReadOnlyList<ReviewImageRequest>? Images = null);

public sealed record ReviewImageResponse(
    Guid Id,
    string PublicId,
    string Url,
    int SortOrder);

/// <summary>
/// A review as shown publicly. Carries the author's display name but never their id, email, or
/// phone: a review is public content, and the customer's identity is not.
/// </summary>
public sealed record ProductReviewSummaryResponse(
    Guid Id,
    Guid ProductId,
    Guid? ProductVariantId,
    string AuthorName,
    int Rating,
    string? Title,
    string Body,
    bool IsVerifiedPurchase,
    int HelpfulCount,
    IReadOnlyList<ReviewImageResponse> Images,
    DateTime? PublishedAtUtc,
    DateTime CreatedAtUtc);

/// <summary>
/// The customer's own review, including its moderation state so the UI can explain why a
/// review is not yet visible.
/// </summary>
public sealed record MyReviewResponse(
    Guid Id,
    Guid ProductId,
    Guid? ProductVariantId,
    string ProductName,
    int Rating,
    string? Title,
    string Body,
    bool IsVerifiedPurchase,
    string Status,
    string? ModerationReason,
    int HelpfulCount,
    IReadOnlyList<ReviewImageResponse> Images,
    DateTime? PublishedAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

/// <summary>
/// Full review as shown in the admin moderation queue, including the author so a moderator can
/// identify who submitted it.
/// </summary>
public sealed record AdminReviewResponse(
    Guid Id,
    Guid CustomerId,
    string CustomerEmail,
    Guid ProductId,
    string ProductName,
    Guid? ProductVariantId,
    int Rating,
    string? Title,
    string Body,
    bool IsVerifiedPurchase,
    string Status,
    string? ModerationReason,
    int HelpfulCount,
    IReadOnlyList<ReviewImageResponse> Images,
    DateTime? PublishedAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

/// <summary>
/// Per-star counts for the ratings breakdown bar chart. Index 0 is unused so the array can be
/// indexed directly by star number.
/// </summary>
public sealed record ReviewRatingBreakdown(IReadOnlyDictionary<int, int> ByRating);

/// <summary>
/// A page of reviews plus the aggregate the UI needs to render the summary block. Both are
/// returned together so the page needs one round trip rather than two.
/// </summary>
public sealed record ReviewListResponse(
    PagedResponse<ProductReviewSummaryResponse> Page,
    decimal RatingAverage,
    int RatingCount,
    ReviewRatingBreakdown Breakdown);

/// <summary>Result of toggling a "this helped" vote.</summary>
public sealed record ReviewHelpfulResponse(bool IsHelpful, int HelpfulCount);

public sealed record ModerateReviewRequest(string Status, string? Reason = null);

/// <summary>
/// Result of a customer review-image upload. The same <c>publicId</c>/<c>url</c> pair is then
/// sent back in <see cref="CreateReviewRequest.Images"/> to attach the image to a review.
/// </summary>
public sealed record ReviewImageUploadResponse(
    string PublicId,
    string Url,
    string? Format,
    int? Width,
    int? Height);