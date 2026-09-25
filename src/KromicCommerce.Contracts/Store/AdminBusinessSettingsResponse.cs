namespace KromicCommerce.Contracts.Store;

/// <summary>
/// Full business settings response for Admin — includes all configuration sections.
/// Never returned to unauthenticated callers.
/// </summary>
public sealed record AdminBusinessSettingsResponse(
    string BusinessName,
    string? LegalName,
    string? WebsiteUrl,
    string? SupportEmail,
    string? SupportPhone,
    string? Address,
    string? LogoUrl,
    string CountryCode,
    string CurrencyCode,
    string TimeZoneId,
    string Culture,
    bool IsStoreOpen,
    string? TemporaryClosureMessage,
    string? FacebookUrl,
    string? InstagramUrl,
    string? TwitterUrl,
    string? YoutubeUrl,
    DateTime UpdatedAtUtc,
    DeliverySettingsDto Delivery,
    StoreAuthSettingsDto Auth,
    EmailSettingsDto Email,
    SeoSettingsDto Seo);
