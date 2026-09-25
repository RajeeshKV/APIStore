using KromicCommerce.Domain.Store;

namespace KromicCommerce.Application.Abstractions.Commerce;

/// <summary>
/// Centralised tax calculation service.
/// All checkout and storefront code that needs tax information must use this service.
/// Do not duplicate tax logic in controllers or handlers.
///
/// Two modes:
///   IsPriceInclusive = false → tax is added on top of the taxable subtotal.
///   IsPriceInclusive = true  → tax is extracted from prices already inclusive of tax.
/// </summary>
public interface ITaxCalculationService
{
    /// <summary>
    /// Calculates tax for the given taxable subtotal using the provided tax settings.
    /// </summary>
    /// <param name="taxableSubtotal">
    /// The subtotal after discount that is subject to tax.
    /// For exclusive pricing this is: (subtotal - discount).
    /// For inclusive pricing this is the gross amount; tax is extracted from it.
    /// </param>
    /// <param name="taxSettings">Current store tax configuration.</param>
    TaxCalculationResult Calculate(decimal taxableSubtotal, TaxSettings taxSettings);
}

/// <summary>Immutable result of a tax calculation.</summary>
public sealed record TaxCalculationResult(
    /// <summary>The amount of tax. 0 when tax is disabled.</summary>
    decimal TaxAmount,

    /// <summary>The effective tax percentage used (0 when disabled).</summary>
    decimal EffectiveTaxPercentage,

    /// <summary>Whether tax was included in the product prices (extracted vs added).</summary>
    bool IsPriceInclusive,

    /// <summary>Display label (e.g. "GST", "VAT", "Tax").</summary>
    string TaxLabel);
