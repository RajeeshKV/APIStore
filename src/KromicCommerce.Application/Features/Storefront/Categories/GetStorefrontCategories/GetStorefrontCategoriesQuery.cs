namespace KromicCommerce.Application.Features.Storefront.Categories.GetStorefrontCategories;

public sealed record GetStorefrontCategoriesQuery
    : IQuery<IReadOnlyList<StorefrontCategoryResponse>>;
