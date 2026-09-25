namespace KromicCommerce.Application.Features.Store.SetStoreOpen;

internal sealed class SetStoreOpenHandler(
    IApplicationDbContext db,
    IBusinessSettingsService settingsService,
    ILogger<SetStoreOpenHandler> logger)
    : ICommandHandler<SetStoreOpenCommand>
{
    public async Task<Result> Handle(
        SetStoreOpenCommand command,
        CancellationToken cancellationToken)
    {
        var settings = await db.BusinessSettings
            .FindAsync([BusinessSettings.SingletonId], cancellationToken);

        if (settings is null)
            return Result.Failure(Error.NotFound(
                "BUSINESS_SETTINGS_NOT_FOUND", "Business settings have not been initialised."));

        settings.SetStoreOpen(command.IsOpen, command.ClosureMessage);
        await db.SaveChangesAsync(cancellationToken);
        settingsService.Invalidate();

        logger.LogInformation("Store status set to {Status}.", command.IsOpen ? "Open" : "Closed");
        return Result.Success();
    }
}
