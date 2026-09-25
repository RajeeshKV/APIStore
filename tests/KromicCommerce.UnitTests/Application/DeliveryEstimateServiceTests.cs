using KromicCommerce.Application.Services;

namespace KromicCommerce.UnitTests.Application;

public sealed class DeliveryEstimateServiceTests
{
    private readonly DeliveryEstimateService _svc = new();
    private static readonly DateOnly Today = new(2026, 10, 1);

    [Fact]
    public void Calculate_returns_from_and_to_dates()
    {
        var result = _svc.Calculate(1, 3, 7, Today);

        result.Should().NotBeNull();
        result!.From.Should().Be("2026-10-05"); // today + 1 + 3
        result.To.Should().Be("2026-10-09");    // today + 1 + 7
    }

    [Fact]
    public void Calculate_range_description_for_different_min_max()
    {
        var result = _svc.Calculate(1, 3, 7, Today);
        result!.Description.Should().Contain("4").And.Subject.Should().Contain("8");
    }

    [Fact]
    public void Calculate_single_day_description_when_min_equals_max()
    {
        var result = _svc.Calculate(0, 5, 5, Today);
        result!.Description.Should().Contain("5 business day");
        result.From.Should().Be(result.To);
    }

    [Fact]
    public void Calculate_zero_processing_uses_just_delivery_days()
    {
        var result = _svc.Calculate(0, 2, 4, Today);
        result!.From.Should().Be("2026-10-03"); // today + 0 + 2
        result.To.Should().Be("2026-10-05");    // today + 0 + 4
    }

    [Fact]
    public void Calculate_uses_current_utc_date_when_reference_is_null()
    {
        // Just verifies it doesn't throw — we can't assert exact dates here
        var result = _svc.Calculate(1, 3, 7);
        result.Should().NotBeNull();
        result!.From.Should().NotBeNullOrEmpty();
        result.To.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Calculate_returns_estimate_even_when_all_days_zero()
    {
        var result = _svc.Calculate(0, 0, 0, Today);
        result.Should().NotBeNull();
        result!.From.Should().Be("2026-10-01");
        result.To.Should().Be("2026-10-01");
    }
}
