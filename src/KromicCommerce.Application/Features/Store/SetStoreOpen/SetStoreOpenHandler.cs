using KromicCommerce.Application.Features.Store.GetAdminSettings;

namespace KromicCommerce.Application.Features.Store.SetStoreOpen;

internal sealed class SetStoreOpenHandler(
    IApplicationDbContext db,
    IBusinessSettingsService settingsService,
    ILogger<SetStoreOpenHandler> logger)
    : ICommandHandler<SetStoreOpenCommand, AdminBusinessSettingsResponse>
{
    public async Task<Result<AdminBusinessSettingsResponse>> Handle(
        SetStoreOpenCommand command,
        CancellationToken cancellationToken)
    {
        var settings = await db.BusinessSettings
            .FindAsync([BusinessSettings.SingletonId], cancellationToken);

        if (settings is null)
            return Result.Failure<AdminBusinessSettingsResponse>(Error.NotFound(
                "BUSINESS_SETTINGS_NOT_FOUND", "Business settings have not been initialised."));

        settings.SetStoreOpen(command.IsOpen, command.ClosureMessage);
        await db.SaveChangesAsync(cancellationToken);
        settingsService.Invalidate();
        logger.LogInformation("Store status set to {Status}.", command.IsOpen ? "Open" : "Closed");

        var updated = await settingsService.GetAsync(cancellationToken);
        return Result.Success(AdminSettingsMapper.Map(updated!));
    }
}
