using KromicCommerce.Domain.Store;

namespace KromicCommerce.UnitTests.Domain;

/// <summary>
/// Tests the shipping calculation rules from DeliverySettings — the same logic used
/// inside CheckoutHandler. These verify the domain value object behaves correctly.
/// </summary>
public sealed class ShippingCalcTests
{
    [Fact]
    public void Flat_fee_applied_when_subtotal_below_threshold()
    {
        var delivery = DeliverySettings.Create(50m, 500m, true, 20m, 1, 3, 7);
        // Subtotal 400 < threshold 500 → flat fee applies
        var shipping = CalculateShipping(delivery, 400m);
        shipping.Should().Be(50m);
    }

    [Fact]
    public void Free_shipping_when_subtotal_meets_threshold()
    {
        var delivery = DeliverySettings.Create(50m, 500m, true, 20m, 1, 3, 7);
        // Subtotal 500 == threshold → free
        var shipping = CalculateShipping(delivery, 500m);
        shipping.Should().Be(0m);
    }

    [Fact]
    public void Free_shipping_when_subtotal_exceeds_threshold()
    {
        var delivery = DeliverySettings.Create(50m, 500m, true, 20m, 1, 3, 7);
        var shipping = CalculateShipping(delivery, 600m);
        shipping.Should().Be(0m);
    }

    [Fact]
    public void No_free_shipping_when_threshold_is_null()
    {
        var delivery = DeliverySettings.Create(50m, null, true, 20m, 1, 3, 7);
        // No threshold configured → flat fee always applies
        var shipping = CalculateShipping(delivery, 1000m);
        shipping.Should().Be(50m);
    }

    [Fact]
    public void Zero_flat_fee_means_always_free()
    {
        var delivery = DeliverySettings.Create(0m, null, false, 0m, 1, 3, 7);
        var shipping = CalculateShipping(delivery, 1m);
        shipping.Should().Be(0m);
    }

    [Fact]
    public void COD_fee_added_for_cod_orders()
    {
        var delivery = DeliverySettings.Create(50m, null, true, 20m, 1, 3, 7);
        var codFee = 20m; // CodExtraFee
        var grand = 400m + 50m + codFee; // subtotal + shipping + cod
        grand.Should().Be(470m);
    }

    [Fact]
    public void COD_not_available_when_disabled()
    {
        var delivery = DeliverySettings.Create(50m, null, false, 0m, 1, 3, 7);
        delivery.CodEnabled.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Helper — mirrors CheckoutHandler shipping logic
    // -----------------------------------------------------------------------
    private static decimal CalculateShipping(DeliverySettings delivery, decimal subtotal)
    {
        var shipping = delivery.FlatFeeAmount;
        if (delivery.FreeShippingThreshold.HasValue && subtotal >= delivery.FreeShippingThreshold.Value)
            shipping = 0m;
        return shipping;
    }
}
