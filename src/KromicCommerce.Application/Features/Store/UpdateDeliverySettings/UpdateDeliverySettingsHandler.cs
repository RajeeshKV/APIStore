using KromicCommerce.Application.Features.Store.GetAdminSettings;

namespace KromicCommerce.Application.Features.Store.UpdateDeliverySettings;

internal sealed class UpdateDeliverySettingsHandler(
    IApplicationDbContext db,
    IBusinessSettingsService settingsService,
    ILogger<UpdateDeliverySettingsHandler> logger)
    : ICommandHandler<UpdateDeliverySettingsCommand, AdminBusinessSettingsResponse>
{
    public async Task<Result<AdminBusinessSettingsResponse>> Handle(
        UpdateDeliverySettingsCommand command,
        CancellationToken cancellationToken)
    {
        var settings = await db.BusinessSettings
            .FindAsync([BusinessSettings.SingletonId], cancellationToken);

        if (settings is null)
            return Result.Failure<AdminBusinessSettingsResponse>(Error.NotFound(
                "BUSINESS_SETTINGS_NOT_FOUND", "Business settings have not been initialised."));

        var delivery = DeliverySettings.Create(
            command.FlatFeeAmount, command.FreeShippingThreshold,
            command.CodEnabled, command.CodExtraFee,
            command.ProcessingDays, command.MinDeliveryDays, command.MaxDeliveryDays);

        settings.UpdateDelivery(delivery);
        await db.SaveChangesAsync(cancellationToken);
        settingsService.Invalidate();
        logger.LogInformation("BusinessSettings delivery configuration updated.");

        var updated = await settingsService.GetAsync(cancellationToken);
        return Result.Success(AdminSettingsMapper.Map(updated!));
    }
}
