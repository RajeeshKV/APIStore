namespace KromicCommerce.Contracts.Catalog;

public sealed record UpdateBrandRequest(
    string Name,
    string Slug,
    string? Description,
    string? WebsiteUrl,
    bool IsActive);
