using KromicCommerce.Application.Features.Store.GetAdminSettings;

namespace KromicCommerce.Application.Features.Store.UpdateEmailSettings;

internal sealed class UpdateEmailSettingsHandler(
    IApplicationDbContext db,
    IBusinessSettingsService settingsService,
    ILogger<UpdateEmailSettingsHandler> logger)
    : ICommandHandler<UpdateEmailSettingsCommand, AdminBusinessSettingsResponse>
{
    public async Task<Result<AdminBusinessSettingsResponse>> Handle(
        UpdateEmailSettingsCommand command,
        CancellationToken cancellationToken)
    {
        var settings = await db.BusinessSettings
            .FindAsync([BusinessSettings.SingletonId], cancellationToken);

        if (settings is null)
            return Result.Failure<AdminBusinessSettingsResponse>(Error.NotFound(
                "BUSINESS_SETTINGS_NOT_FOUND", "Business settings have not been initialised."));

        var email = EmailSettings.Create(command.Mode, command.SenderName, command.SenderEmail);
        settings.UpdateEmail(email);
        await db.SaveChangesAsync(cancellationToken);
        settingsService.Invalidate();
        logger.LogInformation("BusinessSettings email configuration updated. Mode: {Mode}", command.Mode);

        var updated = await settingsService.GetAsync(cancellationToken);
        return Result.Success(AdminSettingsMapper.Map(updated!));
    }
}
