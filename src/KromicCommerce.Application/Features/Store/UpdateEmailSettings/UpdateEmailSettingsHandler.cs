namespace KromicCommerce.Application.Features.Store.UpdateEmailSettings;

internal sealed class UpdateEmailSettingsHandler(
    IApplicationDbContext db,
    IBusinessSettingsService settingsService,
    ILogger<UpdateEmailSettingsHandler> logger)
    : ICommandHandler<UpdateEmailSettingsCommand>
{
    public async Task<Result> Handle(
        UpdateEmailSettingsCommand command,
        CancellationToken cancellationToken)
    {
        var settings = await db.BusinessSettings
            .FindAsync([BusinessSettings.SingletonId], cancellationToken);

        if (settings is null)
            return Result.Failure(Error.NotFound(
                "BUSINESS_SETTINGS_NOT_FOUND", "Business settings have not been initialised."));

        if (!Enum.TryParse<EmailMode>(command.Mode, ignoreCase: false, out var mode))
            return Result.Failure(Error.Validation(
                "INVALID_EMAIL_MODE", $"Unknown email mode: {command.Mode}."));

        var email = EmailSettings.Create(mode, command.SenderName, command.SenderEmail);
        settings.UpdateEmail(email);
        await db.SaveChangesAsync(cancellationToken);
        settingsService.Invalidate();

        logger.LogInformation("BusinessSettings email configuration updated. Mode: {Mode}", command.Mode);
        return Result.Success();
    }
}
