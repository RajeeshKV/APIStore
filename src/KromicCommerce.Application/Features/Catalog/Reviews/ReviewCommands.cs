namespace KromicCommerce.Application.Features.Catalog.Reviews;

// -------------------------------------------------------------------------
// Customer commands
// -------------------------------------------------------------------------
// Every CustomerId here comes from ICurrentUserService at the controller. No request DTO
// carries a customer id, so a caller cannot submit, edit, or delete as another customer.

public sealed record SubmitReviewCommand(
    Guid CustomerId,
    Guid ProductId,
    Guid? ProductVariantId,
    int Rating,
    string? Title,
    string Body,
    IReadOnlyList<ReviewImageRequest> Images) : ICommand<MyReviewResponse>;

public sealed record EditReviewCommand(
    Guid CustomerId,
    Guid ReviewId,
    int Rating,
    string? Title,
    string Body,
    IReadOnlyList<ReviewImageRequest> Images) : ICommand<MyReviewResponse>;

public sealed record DeleteReviewCommand(Guid CustomerId, Guid ReviewId) : ICommand;

/// <summary>
/// Toggles the caller's helpful vote. A toggle rather than separate set/unset endpoints so the
/// UI's single button cannot drift out of sync with the server.
/// </summary>
public sealed record ToggleReviewHelpfulCommand(Guid CustomerId, Guid ReviewId)
    : ICommand<ReviewHelpfulResponse>;

// -------------------------------------------------------------------------
// Admin commands
// -------------------------------------------------------------------------

public sealed record ModerateReviewCommand(Guid ReviewId, ReviewStatus Status, string? Reason)
    : ICommand<AdminReviewResponse>;

public sealed record AdminDeleteReviewCommand(Guid ReviewId) : ICommand;

// -------------------------------------------------------------------------
// Queries
// -------------------------------------------------------------------------

/// <summary>
/// Public listing. Only Published reviews are ever returned.
/// Sort is one of recent | helpful | rating, each with a deterministic tail so paging is stable.
/// </summary>
public sealed record GetProductReviewsQuery(
    Guid ProductId,
    int Page,
    int PageSize,
    string? Sort = null,
    int? Rating = null) : IQuery<ReviewListResponse>;

public sealed record GetMyReviewsQuery(Guid CustomerId, int Page, int PageSize)
    : IQuery<PagedResponse<MyReviewResponse>>;

public sealed record GetAdminReviewsQuery(
    ReviewStatus? Status,
    Guid? ProductId,
    int? Rating,
    string? Search,
    int Page,
    int PageSize) : IQuery<PagedResponse<AdminReviewResponse>>;

public sealed record GetAdminReviewQuery(Guid ReviewId) : IQuery<AdminReviewResponse>;

// -------------------------------------------------------------------------
// Validators
// -------------------------------------------------------------------------

internal sealed class SubmitReviewValidator : AbstractValidator<SubmitReviewCommand>
{
    public SubmitReviewValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Rating).InclusiveBetween(1, 5)
            .WithMessage("Rating must be between 1 and 5.");
        RuleFor(x => x.Body).NotEmpty().MaximumLength(ProductReview.BodyMaxLength);
        RuleFor(x => x.Title).MaximumLength(ProductReview.TitleMaxLength);
        RuleFor(x => x.Images)
            .Must(i => i is null || i.Count <= ProductReview.MaxImages)
            .WithMessage($"At most {ProductReview.MaxImages} images are allowed.");
    }
}

internal sealed class EditReviewValidator : AbstractValidator<EditReviewCommand>
{
    public EditReviewValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.ReviewId).NotEmpty();
        RuleFor(x => x.Rating).InclusiveBetween(1, 5)
            .WithMessage("Rating must be between 1 and 5.");
        RuleFor(x => x.Body).NotEmpty().MaximumLength(ProductReview.BodyMaxLength);
        RuleFor(x => x.Title).MaximumLength(ProductReview.TitleMaxLength);
        RuleFor(x => x.Images)
            .Must(i => i is null || i.Count <= ProductReview.MaxImages)
            .WithMessage($"At most {ProductReview.MaxImages} images are allowed.");
    }
}

internal sealed class ModerateReviewValidator : AbstractValidator<ModerateReviewCommand>
{
    public ModerateReviewValidator()
    {
        RuleFor(x => x.ReviewId).NotEmpty();
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Reason)
            .MaximumLength(ProductReview.ModerationReasonMaxLength);
        RuleFor(x => x.Reason)
            .NotEmpty()
            .WithMessage("A reason is required when rejecting a review.")
            .When(x => x.Status == ReviewStatus.Rejected);
    }
}

internal sealed class GetProductReviewsQueryValidator : AbstractValidator<GetProductReviewsQuery>
{
    public GetProductReviewsQueryValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).GreaterThanOrEqualTo(1).LessThanOrEqualTo(50);
        RuleFor(x => x.Rating)
            .Must(r => r is null or >= 1 and <= 5)
            .WithMessage("Rating must be between 1 and 5.");
        // Whitelisted rather than parsed, so a caller cannot pass an arbitrary sort key through
        // into an ORDER BY.
        RuleFor(x => x.Sort)
            .Must(s => s is null
                || s.Equals("recent", StringComparison.OrdinalIgnoreCase)
                || s.Equals("helpful", StringComparison.OrdinalIgnoreCase)
                || s.Equals("rating", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Sort must be one of: recent, helpful, rating.");
    }
}

internal sealed class GetMyReviewsQueryValidator : AbstractValidator<GetMyReviewsQuery>
{
    public GetMyReviewsQueryValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).GreaterThanOrEqualTo(1).LessThanOrEqualTo(50);
    }
}

internal sealed class GetAdminReviewsQueryValidator : AbstractValidator<GetAdminReviewsQuery>
{
    public GetAdminReviewsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).GreaterThanOrEqualTo(1).LessThanOrEqualTo(100);
        RuleFor(x => x.Rating)
            .Must(r => r is null or >= 1 and <= 5)
            .WithMessage("Rating must be between 1 and 5.");
        RuleFor(x => x.Search).MaximumLength(200);
    }
}