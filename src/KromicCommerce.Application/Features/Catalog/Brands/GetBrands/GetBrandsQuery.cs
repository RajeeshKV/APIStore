namespace KromicCommerce.Application.Features.Catalog.Brands.GetBrands;

public sealed record GetBrandsQuery(bool ActiveOnly = true) : IQuery<IReadOnlyList<BrandResponse>>;
public sealed record GetBrandBySlugQuery(string Slug) : IQuery<BrandResponse>;
public sealed record GetBrandByIdQuery(Guid Id) : IQuery<BrandResponse>;
