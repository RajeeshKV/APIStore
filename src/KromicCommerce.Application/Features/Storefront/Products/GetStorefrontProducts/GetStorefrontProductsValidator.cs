namespace KromicCommerce.Application.Features.Storefront.Products.GetStorefrontProducts;

internal sealed class GetStorefrontProductsValidator
    : AbstractValidator<GetStorefrontProductsQuery>
{
    private static readonly HashSet<string> AllowedSortFields =
        ["name", "price", "created_at"];

    public GetStorefrontProductsValidator()
    {
        RuleFor(x => x.Request.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.Request.PageSize).InclusiveBetween(1, 100)
            .WithMessage("Page size must be between 1 and 100.");
        RuleFor(x => x.Request.Search)
            .MaximumLength(200)
            .WithMessage("Search term must not exceed 200 characters.")
            .When(x => x.Request.Search is not null);
        RuleFor(x => x.Request.SortBy)
            .Must(f => f is null || AllowedSortFields.Contains(f.ToLowerInvariant()))
            .WithMessage($"Sort field must be one of: {string.Join(", ", AllowedSortFields)}.")
            .When(x => x.Request.SortBy is not null);
        RuleFor(x => x.Request.SortDirection)
            .Must(d => d is null
                || string.Equals(d, "asc", StringComparison.OrdinalIgnoreCase)
                || string.Equals(d, "desc", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Sort direction must be 'asc' or 'desc'.");
        RuleFor(x => x.Request.MinPrice)
            .GreaterThanOrEqualTo(0)
            .When(x => x.Request.MinPrice.HasValue);
        RuleFor(x => x.Request.MaxPrice)
            .GreaterThanOrEqualTo(0)
            .When(x => x.Request.MaxPrice.HasValue);
    }
}
