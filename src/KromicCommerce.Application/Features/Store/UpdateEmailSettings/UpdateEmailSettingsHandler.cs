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

        EmailSettings email;
        try
        {
            email = EmailSettings.Create(command.Mode, command.SenderName, command.SenderEmail);
        }
        catch (ArgumentException ex)
        {
            // The validator has already rejected these cases, so reaching here means the two
            // layers disagree. Report it as a validation failure rather than letting the
            // exception surface as a 500.
            return Result.Failure<AdminBusinessSettingsResponse>(
                Error.Validation("EMAIL_SETTINGS_INVALID", ex.Message));
        }

        settings.UpdateEmail(email);
        await db.SaveChangesAsync(cancellationToken);
        settingsService.Invalidate();
        logger.LogInformation(
            "BusinessSettings email configuration updated. Mode: {Mode} SenderName: {SenderName}",
            email.Mode, email.SenderName);

        var updated = await settingsService.GetAsync(cancellationToken);
        return Result.Success(AdminSettingsMapper.Map(updated!));
    }
}
