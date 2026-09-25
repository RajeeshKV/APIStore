namespace KromicCommerce.Application.Features.Store.UpdateAuthSettings;

internal sealed class UpdateAuthSettingsHandler(
    IApplicationDbContext db,
    IBusinessSettingsService settingsService,
    ILogger<UpdateAuthSettingsHandler> logger)
    : ICommandHandler<UpdateAuthSettingsCommand>
{
    public async Task<Result> Handle(
        UpdateAuthSettingsCommand command,
        CancellationToken cancellationToken)
    {
        var settings = await db.BusinessSettings
            .FindAsync([BusinessSettings.SingletonId], cancellationToken);

        if (settings is null)
            return Result.Failure(Error.NotFound(
                "BUSINESS_SETTINGS_NOT_FOUND", "Business settings have not been initialised."));

        var auth = StoreAuthSettings.Create(
            command.GoogleOAuthEnabled,
            command.EmailPasswordEnabled,
            command.MobileOtpEnabled,
            command.OtpExpiryMinutes,
            command.OtpResendCooldownSeconds,
            command.OtpMaxAttempts,
            command.SmsProvider);

        settings.UpdateAuth(auth);
        await db.SaveChangesAsync(cancellationToken);
        settingsService.Invalidate();

        logger.LogInformation("BusinessSettings auth configuration updated.");
        return Result.Success();
    }
}
