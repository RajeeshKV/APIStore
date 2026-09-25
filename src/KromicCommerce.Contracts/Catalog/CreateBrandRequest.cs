namespace KromicCommerce.Contracts.Catalog;

public sealed record CreateBrandRequest(
    string Name,
    string Slug,
    string? Description,
    string? WebsiteUrl);
