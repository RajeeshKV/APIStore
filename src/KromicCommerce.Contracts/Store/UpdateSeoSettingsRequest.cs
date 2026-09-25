namespace KromicCommerce.Contracts.Store;

public sealed record UpdateSeoSettingsRequest(
    string? MetaTitle,
    string? MetaDescription,
    string? MetaKeywords,
    string? FaviconUrl,
    string? OgImageUrl);
