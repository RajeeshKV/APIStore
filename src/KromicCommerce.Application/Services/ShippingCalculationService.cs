using KromicCommerce.Application.Abstractions.Catalog;
using KromicCommerce.Application.Abstractions.Commerce;
using KromicCommerce.Domain.Store;

namespace KromicCommerce.Application.Services;

/// <summary>
/// Pure shipping fee and delivery estimate calculation.
/// No I/O — registered as singleton.
/// All monetary arithmetic uses decimal.
/// </summary>
public sealed class ShippingCalculationService(
    IDeliveryEstimateService deliveryEstimateService)
    : IShippingCalculationService
{
    public ShippingCalculationResult Calculate(
        decimal subtotal, bool isCod, DeliverySettings delivery)
    {
        // Free-shipping check
        var isFreeShipping = delivery.FreeShippingThreshold.HasValue
            && subtotal >= delivery.FreeShippingThreshold.Value;

        var shippingAmount = isFreeShipping ? 0m : delivery.FlatFeeAmount;
        var codFee = isCod ? delivery.CodExtraFee : 0m;

        var estimate = deliveryEstimateService.Calculate(
            delivery.ProcessingDays,
            delivery.MinDeliveryDays,
            delivery.MaxDeliveryDays);

        return new ShippingCalculationResult(shippingAmount, isFreeShipping, codFee, estimate);
    }
}
