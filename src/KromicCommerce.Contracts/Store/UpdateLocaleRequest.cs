namespace KromicCommerce.Contracts.Store;

public sealed record UpdateLocaleRequest(
    string CountryCode,
    string CurrencyCode,
    string TimeZoneId,
    string Culture);
