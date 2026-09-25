namespace KromicCommerce.Application.Features.Store.UpdateDeliverySettings;

public sealed record UpdateDeliverySettingsCommand(
    decimal FlatFeeAmount,
    decimal? FreeShippingThreshold,
    bool CodEnabled,
    decimal CodExtraFee,
    int ProcessingDays,
    int MinDeliveryDays,
    int MaxDeliveryDays) : ICommand;
