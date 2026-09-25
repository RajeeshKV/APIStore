using KromicCommerce.Application.Abstractions.Catalog;
using KromicCommerce.Application.Abstractions.Commerce;
using KromicCommerce.Application.Services;

namespace KromicCommerce.UnitTests.Application;

public sealed class ShippingCalculationServiceTests
{
    private static readonly DeliveryEstimateDto SampleEstimate =
        new("2026-10-01", "2026-10-05", "Delivered in 3–7 business days");

    private IShippingCalculationService BuildSut(DeliveryEstimateDto? estimate = null)
    {
        var mockEstimateService = new Mock<IDeliveryEstimateService>();
        mockEstimateService
            .Setup(s => s.Calculate(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateOnly?>()))
            .Returns(estimate ?? SampleEstimate);
        return new ShippingCalculationService(mockEstimateService.Object);
    }

    private static DeliverySettings BuildDelivery(
        decimal flatFee = 50m,
        decimal? freeThreshold = null,
        bool codEnabled = true,
        decimal codFee = 20m,
        int processingDays = 1,
        int minDays = 3,
        int maxDays = 7)
        => DeliverySettings.Create(flatFee, freeThreshold, codEnabled, codFee, processingDays, minDays, maxDays);

    // -----------------------------------------------------------------------
    // Flat fee
    // -----------------------------------------------------------------------

    [Fact]
    public void Calculate_returns_flat_fee_when_below_threshold()
    {
        var sut = BuildSut();
        var delivery = BuildDelivery(flatFee: 50m, freeThreshold: 500m);

        var result = sut.Calculate(subtotal: 200m, isCod: false, delivery);

        result.ShippingAmount.Should().Be(50m);
        result.IsFreeShipping.Should().BeFalse();
    }

    [Fact]
    public void Calculate_returns_zero_shipping_when_threshold_met()
    {
        var sut = BuildSut();
        var delivery = BuildDelivery(flatFee: 50m, freeThreshold: 500m);

        var result = sut.Calculate(subtotal: 500m, isCod: false, delivery);

        result.ShippingAmount.Should().Be(0m);
        result.IsFreeShipping.Should().BeTrue();
    }

    [Fact]
    public void Calculate_returns_zero_shipping_when_subtotal_exceeds_threshold()
    {
        var sut = BuildSut();
        var delivery = BuildDelivery(flatFee: 50m, freeThreshold: 500m);

        var result = sut.Calculate(subtotal: 999m, isCod: false, delivery);

        result.ShippingAmount.Should().Be(0m);
        result.IsFreeShipping.Should().BeTrue();
    }

    [Fact]
    public void Calculate_returns_flat_fee_when_no_threshold_configured()
    {
        var sut = BuildSut();
        var delivery = BuildDelivery(flatFee: 40m, freeThreshold: null);

        var result = sut.Calculate(subtotal: 9999m, isCod: false, delivery);

        result.ShippingAmount.Should().Be(40m);
        result.IsFreeShipping.Should().BeFalse();
    }

    [Fact]
    public void Calculate_returns_zero_shipping_when_flat_fee_is_zero()
    {
        var sut = BuildSut();
        var delivery = BuildDelivery(flatFee: 0m, freeThreshold: null);

        var result = sut.Calculate(subtotal: 100m, isCod: false, delivery);

        result.ShippingAmount.Should().Be(0m);
    }

    // -----------------------------------------------------------------------
    // COD fee
    // -----------------------------------------------------------------------

    [Fact]
    public void Calculate_adds_cod_fee_when_cod_payment()
    {
        var sut = BuildSut();
        var delivery = BuildDelivery(flatFee: 50m, codFee: 25m);

        var result = sut.Calculate(subtotal: 200m, isCod: true, delivery);

        result.CodFee.Should().Be(25m);
    }

    [Fact]
    public void Calculate_cod_fee_is_zero_for_non_cod_payment()
    {
        var sut = BuildSut();
        var delivery = BuildDelivery(flatFee: 50m, codFee: 25m);

        var result = sut.Calculate(subtotal: 200m, isCod: false, delivery);

        result.CodFee.Should().Be(0m);
    }

    [Fact]
    public void Calculate_cod_fee_added_even_when_shipping_is_free()
    {
        var sut = BuildSut();
        var delivery = BuildDelivery(flatFee: 50m, freeThreshold: 200m, codFee: 15m);

        // Subtotal >= threshold → free shipping, but COD fee still applies
        var result = sut.Calculate(subtotal: 500m, isCod: true, delivery);

        result.ShippingAmount.Should().Be(0m);
        result.CodFee.Should().Be(15m);
    }

    // -----------------------------------------------------------------------
    // Delivery estimate passthrough
    // -----------------------------------------------------------------------

    [Fact]
    public void Calculate_includes_delivery_estimate_from_service()
    {
        var estimate = new DeliveryEstimateDto("2026-10-01", "2026-10-07", "3–7 days");
        var sut = BuildSut(estimate);
        var delivery = BuildDelivery();

        var result = sut.Calculate(200m, false, delivery);

        result.DeliveryEstimate.Should().Be(estimate);
    }
}
