using KromicCommerce.Application.Abstractions.Commerce;
using KromicCommerce.Domain.Store;

namespace KromicCommerce.Application.Services;

/// <summary>
/// Centralised tax calculation.
/// No I/O — registered as singleton.
/// All arithmetic uses decimal to avoid floating-point drift.
///
/// Exclusive pricing (IsPriceInclusive = false):
///   tax = subtotal * rate / 100
///
/// Inclusive pricing (IsPriceInclusive = true):
///   tax = subtotal - (subtotal / (1 + rate/100))
///   The displayed subtotal already contains the tax component.
/// </summary>
public sealed class TaxCalculationService : ITaxCalculationService
{
    public TaxCalculationResult Calculate(decimal taxableSubtotal, TaxSettings taxSettings)
    {
        if (!taxSettings.TaxEnabled || taxSettings.TaxPercentage == 0m || taxableSubtotal <= 0m)
            return new TaxCalculationResult(0m, 0m, taxSettings.IsPriceInclusive, taxSettings.TaxLabel);

        decimal taxAmount;

        if (taxSettings.IsPriceInclusive)
        {
            // Tax is extracted from the inclusive price
            // tax = gross - (gross / (1 + rate/100))
            var divisor = 1m + taxSettings.TaxPercentage / 100m;
            taxAmount = taxableSubtotal - Math.Round(taxableSubtotal / divisor, 2, MidpointRounding.ToEven);
        }
        else
        {
            // Tax is added on top
            taxAmount = Math.Round(taxableSubtotal * taxSettings.TaxPercentage / 100m, 2, MidpointRounding.ToEven);
        }

        return new TaxCalculationResult(
            taxAmount,
            taxSettings.TaxPercentage,
            taxSettings.IsPriceInclusive,
            taxSettings.TaxLabel);
    }
}
