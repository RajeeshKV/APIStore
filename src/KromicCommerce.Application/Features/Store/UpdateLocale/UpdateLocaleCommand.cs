namespace KromicCommerce.Application.Features.Store.UpdateLocale;

public sealed record UpdateLocaleCommand(
    string CountryCode,
    string CurrencyCode,
    string TimeZoneId,
    string Culture) : ICommand;
