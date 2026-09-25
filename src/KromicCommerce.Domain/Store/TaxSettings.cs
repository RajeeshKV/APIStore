namespace KromicCommerce.Domain.Store;

/// <summary>
/// Store-level tax configuration.
/// Owned by BusinessSettings and stored as flattened columns.
///
/// Tax model:
///   - Disabled by default (TaxEnabled = false).
///   - TaxPercentage is a non-negative decimal (e.g. 18.0 = 18 %).
///   - IsPriceInclusive = true  → product prices already include tax; tax is extracted, not added.
///   - IsPriceInclusive = false → tax is added on top of the product subtotal.
///
/// Country-agnostic: does not assume GST, VAT, or any specific scheme.
/// All monetary arithmetic must use decimal.
/// </summary>
public sealed class TaxSettings : ValueObject
{
    public static TaxSettings Default() => new()
    {
        TaxEnabled = false,
        TaxPercentage = 0m,
        IsPriceInclusive = false,
        TaxLabel = "Tax"
    };

    /// <summary>Whether tax calculation is active for this store.</summary>
    public bool TaxEnabled { get; private set; }

    /// <summary>Tax rate as a percentage (e.g. 18.0 for 18%). Must be >= 0 and <= 100.</summary>
    public decimal TaxPercentage { get; private set; }

    /// <summary>
    /// When true, product prices already include tax.
    /// Tax is extracted from the subtotal rather than added on top.
    /// When false, tax is added to the subtotal.
    /// </summary>
    public bool IsPriceInclusive { get; private set; }

    /// <summary>Display label for tax (e.g. "GST", "VAT", "Tax"). Shown on receipts.</summary>
    public string TaxLabel { get; private set; } = "Tax";

    // -----------------------------------------------------------------------
    // Factory
    // -----------------------------------------------------------------------

    public static TaxSettings Create(
        bool taxEnabled,
        decimal taxPercentage,
        bool isPriceInclusive,
        string? taxLabel = null)
    {
        if (taxPercentage < 0m)
            throw new ArgumentException("Tax percentage must be >= 0.", nameof(taxPercentage));
        if (taxPercentage > 100m)
            throw new ArgumentException("Tax percentage must be <= 100.", nameof(taxPercentage));
        if (taxEnabled && taxPercentage == 0m)
            throw new ArgumentException("Tax percentage must be > 0 when tax is enabled.", nameof(taxPercentage));

        return new TaxSettings
        {
            TaxEnabled = taxEnabled,
            TaxPercentage = taxPercentage,
            IsPriceInclusive = isPriceInclusive,
            TaxLabel = string.IsNullOrWhiteSpace(taxLabel) ? "Tax" : taxLabel.Trim()
        };
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return TaxEnabled;
        yield return TaxPercentage;
        yield return IsPriceInclusive;
        yield return TaxLabel;
    }
}
