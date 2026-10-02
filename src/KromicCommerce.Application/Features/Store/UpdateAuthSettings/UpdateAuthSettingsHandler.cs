using KromicCommerce.Application.Features.Store.GetAdminSettings;

namespace KromicCommerce.Application.Features.Store.UpdateAuthSettings;

internal sealed class UpdateAuthSettingsHandler(
    IApplicationDbContext db,
    IBusinessSettingsService settingsService,
    ILogger<UpdateAuthSettingsHandler> logger)
    : ICommandHandler<UpdateAuthSettingsCommand, AdminBusinessSettingsResponse>
{
    public async Task<Result<AdminBusinessSettingsResponse>> Handle(
        UpdateAuthSettingsCommand command,
        CancellationToken cancellationToken)
    {
        var settings = await db.BusinessSettings
            .FindAsync([BusinessSettings.SingletonId], cancellationToken);

        if (settings is null)
            return Result.Failure<AdminBusinessSettingsResponse>(Error.NotFound(
                "BUSINESS_SETTINGS_NOT_FOUND", "Business settings have not been initialised."));

        var auth = StoreAuthSettings.Create(
            command.GoogleOAuthEnabled, command.EmailPasswordEnabled,
            command.MobileOtpEnabled, command.OtpExpiryMinutes,
            command.OtpResendCooldownSeconds, command.OtpMaxAttempts,
            // Provider selection is owned by PUT /admin/integrations/sms, which persists the
            // authoritative SmsProviderConfig row. This legacy field is echoed for display only,
            // so a form that omits it must preserve what is already stored rather than blanking it
            // — and must not be forced to invent a value that has no effect on delivery.
            command.SmsProvider ?? settings.Auth.SmsProvider,
            // Preserve existing Google credentials — this handler only changes flags, not credentials
            googleClientId: settings.Auth.GoogleClientId,
            encryptedGoogleClientSecret: settings.Auth.EncryptedGoogleClientSecret,
            googleRedirectUri: settings.Auth.GoogleRedirectUri);

        settings.UpdateAuth(auth);
        await db.SaveChangesAsync(cancellationToken);
        settingsService.Invalidate();
        logger.LogInformation("BusinessSettings auth configuration updated.");

        var updated = await settingsService.GetAsync(cancellationToken);
        return Result.Success(AdminSettingsMapper.Map(updated!));
    }
}
