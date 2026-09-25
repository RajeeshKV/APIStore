namespace KromicCommerce.Application.Features.Storefront.Products.GetStorefrontProductBySlug;

public sealed record GetStorefrontProductBySlugQuery(string Slug)
    : IQuery<StorefrontProductResponse>;
