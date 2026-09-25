namespace KromicCommerce.Application.Features.Catalog.Brands.CreateBrand;

public sealed record CreateBrandCommand(
    string Name,
    string Slug,
    string? Description,
    string? WebsiteUrl) : ICommand<BrandResponse>;
