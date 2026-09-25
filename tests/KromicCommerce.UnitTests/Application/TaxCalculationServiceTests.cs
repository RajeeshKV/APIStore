using KromicCommerce.Application.Abstractions.Commerce;
using KromicCommerce.Application.Services;

namespace KromicCommerce.UnitTests.Application;

public sealed class TaxCalculationServiceTests
{
    private static readonly ITaxCalculationService Sut = new TaxCalculationService();

    // -----------------------------------------------------------------------
    // Disabled tax
    // -----------------------------------------------------------------------

    [Fact]
    public void Calculate_returns_zero_when_tax_disabled()
    {
        var settings = TaxSettings.Create(false, 0m, false);
        var result = Sut.Calculate(1000m, settings);

        result.TaxAmount.Should().Be(0m);
        result.EffectiveTaxPercentage.Should().Be(0m);
    }

    [Fact]
    public void Calculate_returns_zero_for_zero_subtotal()
    {
        var settings = TaxSettings.Create(true, 18m, false);
        var result = Sut.Calculate(0m, settings);

        result.TaxAmount.Should().Be(0m);
    }

    [Fact]
    public void Calculate_returns_zero_for_negative_subtotal()
    {
        var settings = TaxSettings.Create(true, 18m, false);
        var result = Sut.Calculate(-100m, settings);

        result.TaxAmount.Should().Be(0m);
    }

    // -----------------------------------------------------------------------
    // Exclusive pricing (tax added on top)
    // -----------------------------------------------------------------------

    [Fact]
    public void Calculate_exclusive_18_percent_on_1000()
    {
        var settings = TaxSettings.Create(true, 18m, false);
        var result = Sut.Calculate(1000m, settings);

        result.TaxAmount.Should().Be(180m);
        result.EffectiveTaxPercentage.Should().Be(18m);
        result.IsPriceInclusive.Should().BeFalse();
    }

    [Fact]
    public void Calculate_exclusive_5_percent_on_200()
    {
        var settings = TaxSettings.Create(true, 5m, false);
        var result = Sut.Calculate(200m, settings);

        result.TaxAmount.Should().Be(10m);
    }

    [Theory]
    [InlineData(1000.00, 18.0, 180.00)]
    [InlineData(500.00,  12.0,  60.00)]
    [InlineData(299.99,   5.0,  15.00)]  // rounded to 2 dp
    public void Calculate_exclusive_various_rates(decimal subtotal, decimal rate, decimal expectedTax)
    {
        var settings = TaxSettings.Create(true, rate, false);
        var result = Sut.Calculate(subtotal, settings);
        result.TaxAmount.Should().Be(expectedTax);
    }

    // -----------------------------------------------------------------------
    // Inclusive pricing (tax extracted from price)
    // -----------------------------------------------------------------------

    [Fact]
    public void Calculate_inclusive_18_percent_on_1180()
    {
        // 1180 is a price that includes 18% tax on 1000 net
        // tax = 1180 - (1180 / 1.18) = 1180 - 1000 = 180
        var settings = TaxSettings.Create(true, 18m, true);
        var result = Sut.Calculate(1180m, settings);

        result.TaxAmount.Should().Be(180m);
        result.IsPriceInclusive.Should().BeTrue();
    }

    [Fact]
    public void Calculate_inclusive_5_percent_on_210()
    {
        // 210 price inclusive of 5% → net = 200, tax = 10
        var settings = TaxSettings.Create(true, 5m, true);
        var result = Sut.Calculate(210m, settings);

        result.TaxAmount.Should().Be(10m);
    }

    [Fact]
    public void Calculate_inclusive_tax_does_not_equal_exclusive_tax_for_same_base()
    {
        // Inclusive tax on 1000 should be less than exclusive tax on 1000
        var inclusive = TaxSettings.Create(true, 18m, true);
        var exclusive = TaxSettings.Create(true, 18m, false);

        var incResult = Sut.Calculate(1000m, inclusive);
        var excResult = Sut.Calculate(1000m, exclusive);

        incResult.TaxAmount.Should().BeLessThan(excResult.TaxAmount);
    }

    // -----------------------------------------------------------------------
    // Tax label passthrough
    // -----------------------------------------------------------------------

    [Fact]
    public void Calculate_returns_correct_tax_label()
    {
        var settings = TaxSettings.Create(true, 18m, false, "GST");
        var result = Sut.Calculate(1000m, settings);
        result.TaxLabel.Should().Be("GST");
    }

    [Fact]
    public void Calculate_returns_default_label_when_disabled()
    {
        var settings = TaxSettings.Default();
        var result = Sut.Calculate(1000m, settings);
        result.TaxLabel.Should().Be("Tax");
    }

    // -----------------------------------------------------------------------
    // Grand total arithmetic (simulate checkout calculation)
    // -----------------------------------------------------------------------

    [Fact]
    public void Checkout_exclusive_tax_total_is_correct()
    {
        // Subtotal=1000, Discount=100, Shipping=50, Tax=18% on (1000-100)=900
        // Tax = 162, GrandTotal = 900 + 162 + 50 = 1112
        var settings = TaxSettings.Create(true, 18m, false);
        var taxableBase = 1000m - 100m;  // 900
        var taxResult = Sut.Calculate(taxableBase, settings);
        var grandTotal = taxableBase + taxResult.TaxAmount + 50m; // shipping

        taxResult.TaxAmount.Should().Be(162m);
        grandTotal.Should().Be(1112m);
    }

    [Fact]
    public void Checkout_inclusive_tax_total_is_correct()
    {
        // Subtotal=1000 inclusive, Discount=100 → net=900 inclusive
        // Tax extracted from 900 at 18%: tax = 900 - 900/1.18 ≈ 137.29
        // GrandTotal = 900 (already includes tax) + 50 shipping
        var settings = TaxSettings.Create(true, 18m, true);
        var taxableBase = 900m;
        var taxResult = Sut.Calculate(taxableBase, settings);
        var grandTotal = taxableBase + 50m; // shipping only; tax already in price

        grandTotal.Should().Be(950m);
        taxResult.TaxAmount.Should().BeGreaterThan(0m).And.BeLessThan(900m);
    }

    [Fact]
    public void Grand_total_never_goes_negative_with_full_discount()
    {
        var settings = TaxSettings.Create(true, 18m, false);
        var subtotal = 100m;
        var discount = 100m;
        var taxableBase = Math.Max(0m, subtotal - discount); // = 0

        var taxResult = Sut.Calculate(taxableBase, settings);
        var grandTotal = Math.Max(0m, taxableBase + taxResult.TaxAmount);

        grandTotal.Should().Be(0m);
    }
}
