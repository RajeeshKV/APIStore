namespace KromicCommerce.Contracts.Catalog;

public sealed record BrandResponse(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string? WebsiteUrl,
    bool IsActive,
    string? LogoUrl,
    DateTime UpdatedAtUtc);
