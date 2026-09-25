namespace KromicCommerce.Contracts.Store;

public sealed record SeoSettingsDto(
    string? MetaTitle,
    string? MetaDescription,
    string? MetaKeywords,
    string? FaviconUrl,
    string? OgImageUrl);
