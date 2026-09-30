using KromicCommerce.Application.Abstractions.Catalog;
using KromicCommerce.Application.Abstractions.Commerce;
using KromicCommerce.Domain.Store;

namespace KromicCommerce.Application.Services;

/// <summary>
/// Pure shipping fee, COD fee and delivery estimate calculation.
/// No I/O — registered as singleton.
///
/// COD is a shipping concern: the fee is read from
/// <see cref="DeliverySettings.EffectiveCodFee"/>, which is zero whenever COD is disabled.
/// Disabling COD is therefore sufficient to remove the fee from every calculation.
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

        // EffectiveCodFee is 0 unless COD is enabled, so a disabled COD method can never
        // contribute a surcharge even if a caller passes isCod = true.
        var codFee = isCod ? delivery.EffectiveCodFee : 0m;

        var estimate = deliveryEstimateService.Calculate(
            delivery.ProcessingDays,
            delivery.MinDeliveryDays,
            delivery.MaxDeliveryDays);

        return new ShippingCalculationResult(shippingAmount, isFreeShipping, codFee, estimate);
    }
}
