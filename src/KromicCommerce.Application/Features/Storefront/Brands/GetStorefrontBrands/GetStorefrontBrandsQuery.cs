namespace KromicCommerce.Application.Features.Storefront.Brands.GetStorefrontBrands;

public sealed record GetStorefrontBrandsQuery
    : IQuery<IReadOnlyList<StorefrontBrandResponse>>;
