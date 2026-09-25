namespace KromicCommerce.UnitTests.Domain;

public sealed class TaxSettingsDomainTests
{
    // -----------------------------------------------------------------------
    // Default
    // -----------------------------------------------------------------------

    [Fact]
    public void Default_has_tax_disabled_and_zero_percentage()
    {
        var ts = TaxSettings.Default();
        ts.TaxEnabled.Should().BeFalse();
        ts.TaxPercentage.Should().Be(0m);
        ts.IsPriceInclusive.Should().BeFalse();
        ts.TaxLabel.Should().Be("Tax");
    }

    // -----------------------------------------------------------------------
    // Create — valid
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_persists_all_fields()
    {
        var ts = TaxSettings.Create(true, 18m, false, "GST");
        ts.TaxEnabled.Should().BeTrue();
        ts.TaxPercentage.Should().Be(18m);
        ts.IsPriceInclusive.Should().BeFalse();
        ts.TaxLabel.Should().Be("GST");
    }

    [Fact]
    public void Create_uses_Tax_label_when_null_provided()
    {
        var ts = TaxSettings.Create(false, 0m, false, null);
        ts.TaxLabel.Should().Be("Tax");
    }

    [Fact]
    public void Create_uses_Tax_label_when_whitespace_provided()
    {
        var ts = TaxSettings.Create(false, 0m, false, "   ");
        ts.TaxLabel.Should().Be("Tax");
    }

    [Fact]
    public void Create_trims_tax_label()
    {
        var ts = TaxSettings.Create(false, 0m, false, "  VAT  ");
        ts.TaxLabel.Should().Be("VAT");
    }

    [Fact]
    public void Create_allows_zero_percentage_when_disabled()
    {
        var act = () => TaxSettings.Create(false, 0m, false);
        act.Should().NotThrow();
    }

    [Fact]
    public void Create_allows_100_percent_tax()
    {
        var ts = TaxSettings.Create(true, 100m, false);
        ts.TaxPercentage.Should().Be(100m);
    }

    // -----------------------------------------------------------------------
    // Create — validation failures
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_throws_for_negative_percentage()
    {
        var act = () => TaxSettings.Create(false, -1m, false);
        act.Should().Throw<ArgumentException>().WithMessage("*>= 0*");
    }

    [Fact]
    public void Create_throws_for_percentage_above_100()
    {
        var act = () => TaxSettings.Create(false, 101m, false);
        act.Should().Throw<ArgumentException>().WithMessage("*<= 100*");
    }

    [Fact]
    public void Create_throws_when_enabled_with_zero_percentage()
    {
        var act = () => TaxSettings.Create(true, 0m, false);
        act.Should().Throw<ArgumentException>().WithMessage("*> 0 when tax is enabled*");
    }

    // -----------------------------------------------------------------------
    // Value equality
    // -----------------------------------------------------------------------

    [Fact]
    public void Two_identical_settings_are_equal()
    {
        var a = TaxSettings.Create(true, 18m, false, "GST");
        var b = TaxSettings.Create(true, 18m, false, "GST");
        a.Should().Be(b);
    }

    [Fact]
    public void Different_percentages_are_not_equal()
    {
        var a = TaxSettings.Create(true, 18m, false, "GST");
        var b = TaxSettings.Create(true, 5m, false, "GST");
        a.Should().NotBe(b);
    }
}
