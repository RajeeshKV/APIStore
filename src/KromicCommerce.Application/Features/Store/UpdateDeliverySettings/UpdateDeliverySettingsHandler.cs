using KromicCommerce.Application.Features.Store.GetAdminSettings;

namespace KromicCommerce.Application.Features.Store.UpdateDeliverySettings;

internal sealed class UpdateDeliverySettingsHandler(
    IApplicationDbContext db,
    IBusinessSettingsService settingsService,
    ILogger<UpdateDeliverySettingsHandler> logger)
    : ICommandHandler<UpdateDeliverySettingsCommand, AdminBusinessSettingsResponse>
{
    /// <summary>
    /// The single authoritative mutation for cash-on-delivery. It sets CodEnabled and
    /// CodExtraFee together, alongside the rest of the shipping configuration, because COD is a
    /// shipping concern. No other command or endpoint may change COD — see
    /// SingleCodConfigurationPathTests, which enforces that.
    /// </summary>
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
        // Shipping/COD configuration: invalidate the settings object AND every downstream
        // entry that embeds shipping-derived data (storefront delivery estimates).
        settingsService.InvalidateShipping();
        logger.LogInformation(
            "BusinessSettings delivery configuration updated. CodEnabled: {CodEnabled} CodExtraFee: {CodExtraFee}",
            delivery.CodEnabled, delivery.CodExtraFee);

        var updated = await settingsService.GetAsync(cancellationToken);
        return Result.Success(AdminSettingsMapper.Map(updated!));
    }
}
