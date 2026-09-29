using KromicCommerce.Application.Features.Store.GetAdminSettings;

namespace KromicCommerce.Application.Features.Store.UpdateBasicInfo;

internal sealed class UpdateBasicInfoHandler(
    IApplicationDbContext db,
    IBusinessSettingsService settingsService,
    ILogger<UpdateBasicInfoHandler> logger)
    : ICommandHandler<UpdateBasicInfoCommand, AdminBusinessSettingsResponse>
{
    public async Task<Result<AdminBusinessSettingsResponse>> Handle(
        UpdateBasicInfoCommand command,
        CancellationToken cancellationToken)
    {
        var settings = await db.BusinessSettings
            .FindAsync([BusinessSettings.SingletonId], cancellationToken);

        if (settings is null)
            return Result.Failure<AdminBusinessSettingsResponse>(Error.NotFound(
                "BUSINESS_SETTINGS_NOT_FOUND", "Business settings have not been initialised."));

        settings.UpdateBasicInfo(
            command.BusinessName, command.LegalName, command.WebsiteUrl,
            command.SupportEmail, command.SupportPhone, command.Address);
        settings.UpdateSocialLinks(
            command.FacebookUrl, command.InstagramUrl,
            command.TwitterUrl, command.YoutubeUrl,
            command.WhatsAppNumber, command.LinkedInUrl);

        await db.SaveChangesAsync(cancellationToken);
        settingsService.Invalidate();
        logger.LogInformation("BusinessSettings basic info updated.");

        var updated = await settingsService.GetAsync(cancellationToken);
        return Result.Success(AdminSettingsMapper.Map(updated!));
    }
}
