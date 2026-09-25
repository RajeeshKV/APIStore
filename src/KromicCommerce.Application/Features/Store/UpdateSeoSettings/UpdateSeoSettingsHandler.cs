namespace KromicCommerce.Application.Features.Store.UpdateSeoSettings;

internal sealed class UpdateSeoSettingsHandler(
    IApplicationDbContext db,
    IBusinessSettingsService settingsService,
    ILogger<UpdateSeoSettingsHandler> logger)
    : ICommandHandler<UpdateSeoSettingsCommand>
{
    public async Task<Result> Handle(
        UpdateSeoSettingsCommand command,
        CancellationToken cancellationToken)
    {
        var settings = await db.BusinessSettings
            .FindAsync([BusinessSettings.SingletonId], cancellationToken);

        if (settings is null)
            return Result.Failure(Error.NotFound(
                "BUSINESS_SETTINGS_NOT_FOUND", "Business settings have not been initialised."));

        var seo = SeoSettings.Create(
            command.MetaTitle,
            command.MetaDescription,
            command.MetaKeywords,
            command.FaviconUrl,
            command.OgImageUrl);

        settings.UpdateSeo(seo);
        await db.SaveChangesAsync(cancellationToken);
        settingsService.Invalidate();

        logger.LogInformation("BusinessSettings SEO configuration updated.");
        return Result.Success();
    }
}
