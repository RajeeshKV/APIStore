namespace KromicCommerce.Application.Features.Me.Wishlist;

/// <summary>
/// Customer id is always supplied by the controller from ICurrentUserService. It is a command
/// parameter rather than something the handler reads from the current-user service itself so
/// the ownership rule stays visible in the call site and so handlers remain unit-testable.
/// </summary>
public sealed record AddWishlistItemCommand(
    Guid CustomerId,
    Guid ProductId,
    Guid? ProductVariantId) : ICommand<AddWishlistItemResponse>;

public sealed record RemoveWishlistItemCommand(
    Guid CustomerId,
    Guid ProductId,
    Guid? ProductVariantId) : ICommand;

public sealed record ClearWishlistCommand(Guid CustomerId) : ICommand;

public sealed record GetWishlistQuery(Guid CustomerId, int Page, int PageSize)
    : IQuery<PagedResponse<WishlistItemResponse>>;

/// <summary>
/// Bulk membership check. ProductIds is intentionally not capped here beyond the validator:
/// the cost is a single indexed lookup regardless of list length.
/// </summary>
public sealed record GetWishlistStatusQuery(Guid CustomerId, IReadOnlyList<Guid> ProductIds)
    : IQuery<WishlistStatusResponse>;

internal sealed class AddWishlistItemValidator : AbstractValidator<AddWishlistItemCommand>
{
    public AddWishlistItemValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty().WithMessage("A product is required.");
    }
}

internal sealed class GetWishlistQueryValidator : AbstractValidator<GetWishlistQuery>
{
    public GetWishlistQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).GreaterThanOrEqualTo(1).LessThanOrEqualTo(100);
    }
}

internal sealed class GetWishlistStatusQueryValidator : AbstractValidator<GetWishlistStatusQuery>
{
    public GetWishlistStatusQueryValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        // Bounded so a single request cannot ask the database about an unbounded id list.
        RuleFor(x => x.ProductIds)
            .Must(ids => ids.Count <= 200)
            .WithMessage("At most 200 product ids may be checked at once.");
    }
}