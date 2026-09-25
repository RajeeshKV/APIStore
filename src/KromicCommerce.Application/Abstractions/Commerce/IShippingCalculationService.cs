using KromicCommerce.Domain.Store;

namespace KromicCommerce.Application.Abstractions.Commerce;

/// <summary>
/// Centralised shipping fee and delivery estimate calculation.
/// All checkout and storefront code that needs shipping information must use this service.
/// Do not duplicate shipping logic in controllers, handlers, or mapping helpers.
/// </summary>
public interface IShippingCalculationService
{
    /// <summary>
    /// Calculates the shipping cost for the given subtotal and payment method.
    /// </summary>
    /// <param name="subtotal">Order subtotal before discount and tax (server-calculated).</param>
    /// <param name="isCod">True when the customer chose Cash on Delivery.</param>
    /// <param name="delivery">Current store delivery settings.</param>
    ShippingCalculationResult Calculate(decimal subtotal, bool isCod, DeliverySettings delivery);
}

/// <summary>Immutable result of a shipping calculation.</summary>
public sealed record ShippingCalculationResult(
    /// <summary>Shipping fee to charge. 0 when free shipping applies.</summary>
    decimal ShippingAmount,

    /// <summary>Whether free-shipping threshold was met.</summary>
    bool IsFreeShipping,

    /// <summary>COD surcharge (0 when payment is not COD or no surcharge configured).</summary>
    decimal CodFee,

    /// <summary>Estimated delivery window using processing + min/max delivery days.</summary>
    DeliveryEstimateDto? DeliveryEstimate);
