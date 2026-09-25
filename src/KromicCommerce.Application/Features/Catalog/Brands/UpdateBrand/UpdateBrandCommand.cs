namespace KromicCommerce.Application.Features.Catalog.Brands.UpdateBrand;

public sealed record UpdateBrandCommand(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string? WebsiteUrl,
    bool IsActive) : ICommand;
