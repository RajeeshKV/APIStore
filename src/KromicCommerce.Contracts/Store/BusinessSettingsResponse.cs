namespace KromicCommerce.Contracts.Store;

public sealed record BusinessSettingsResponse(
    string BusinessName,
    string? LegalName,
    string CountryCode,
    string CurrencyCode,
    string TimeZoneId,
    string Culture,
    bool IsStoreOpen,
    string? SupportEmail,
    string? SupportPhone,
    string? LogoUrl);
