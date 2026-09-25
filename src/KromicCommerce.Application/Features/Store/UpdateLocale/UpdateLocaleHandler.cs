namespace KromicCommerce.Application.Features.Store.UpdateLocale;

internal sealed class UpdateLocaleHandler(
    IApplicationDbContext db,
    IBusinessSettingsService settingsService,
    ILogger<UpdateLocaleHandler> logger)
    : ICommandHandler<UpdateLocaleCommand>
{
    public async Task<Result> Handle(
        UpdateLocaleCommand command,
        CancellationToken cancellationToken)
    {
        var settings = await db.BusinessSettings
            .FindAsync([BusinessSettings.SingletonId], cancellationToken);

        if (settings is null)
            return Result.Failure(Error.NotFound(
                "BUSINESS_SETTINGS_NOT_FOUND", "Business settings have not been initialised."));

        settings.UpdateLocale(
            command.CountryCode,
            command.CurrencyCode,
            command.TimeZoneId,
            command.Culture);

        await db.SaveChangesAsync(cancellationToken);
        settingsService.Invalidate();

        logger.LogInformation("BusinessSettings locale updated to {Country}/{Currency}.",
            command.CountryCode, command.CurrencyCode);

        return Result.Success();
    }
}
