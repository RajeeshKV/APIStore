namespace KromicCommerce.Application.Features.Store.UpdateSeoSettings;

public sealed record UpdateSeoSettingsCommand(
    string? MetaTitle,
    string? MetaDescription,
    string? MetaKeywords,
    string? FaviconUrl,
    string? OgImageUrl) : ICommand;
