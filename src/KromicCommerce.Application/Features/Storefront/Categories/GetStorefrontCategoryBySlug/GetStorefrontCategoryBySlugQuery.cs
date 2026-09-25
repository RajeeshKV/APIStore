namespace KromicCommerce.Application.Features.Storefront.Categories.GetStorefrontCategoryBySlug;

public sealed record GetStorefrontCategoryBySlugQuery(string Slug)
    : IQuery<StorefrontCategoryResponse>;
