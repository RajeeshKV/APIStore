namespace KromicCommerce.Application.Features.Storefront.Brands.GetStorefrontBrandBySlug;

public sealed record GetStorefrontBrandBySlugQuery(string Slug)
    : IQuery<StorefrontBrandResponse>;
