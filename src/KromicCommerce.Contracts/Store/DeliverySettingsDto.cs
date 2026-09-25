namespace KromicCommerce.Contracts.Store;

public sealed record DeliverySettingsDto(
    decimal FlatFeeAmount,
    decimal? FreeShippingThreshold,
    bool CodEnabled,
    decimal CodExtraFee,
    int ProcessingDays,
    int MinDeliveryDays,
    int MaxDeliveryDays);
