namespace KromicCommerce.UnitTests.Domain;

/// <summary>
/// Cash-on-delivery configuration rules.
///
/// COD is a shipping concern: availability and the surcharge both live in DeliverySettings,
/// and DeliverySettings is the only place either may be read from. These tests pin the two
/// properties that make a single source of truth workable — disabling COD removes the fee
/// everywhere, and toggling availability never disturbs the rest of the shipping config.
/// </summary>
public sealed class CashOnDeliveryConfigurationTests
{
    // -----------------------------------------------------------------------
    // Disabled COD carries no fee
    // -----------------------------------------------------------------------

    /// <summary>
    /// The whole point of EffectiveCodFee: with COD off, nothing may be charged for it. The
    /// configured fee is retained for later re-enablement, but it must not reach a price.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(99.99)]
    [InlineData(500)]
    public void Disabling_COD_makes_the_configured_fee_unchargeable(decimal configuredFee)
    {
        var delivery = DeliverySettings.Create(
            flatFeeAmount: 50m,
            freeShippingThreshold: null,
            codEnabled: false,
            codExtraFee: configuredFee,
            processingDays: 1,
            minDeliveryDays: 3,
            maxDeliveryDays: 7);

        // The fee is remembered, so re-enabling COD restores what the merchant configured.
        delivery.CodExtraFee.Should().Be(configuredFee);

        // But it can never be charged while COD is off.
        delivery.EffectiveCodFee.Should().Be(0m);
        delivery.IsCodAvailable.Should().BeFalse();
    }

    [Fact]
    public void Enabled_COD_keeps_its_configured_fee()
    {
        var delivery = DeliverySettings.Create(50m, null, codEnabled: true, codExtraFee: 25m, 1, 3, 7);

        delivery.CodExtraFee.Should().Be(25m);
        delivery.EffectiveCodFee.Should().Be(25m);
        delivery.IsCodAvailable.Should().BeTrue();
    }

    /// <summary>
    /// An admin UI that still submits the previously configured fee while the store has COD off
    /// must be accepted, not rejected. Normalising keeps that save working; throwing would
    /// strand the admin on a settings screen they cannot submit.
    /// </summary>
    [Fact]
    public void Disabling_COD_accepts_a_resubmitted_fee_instead_of_rejecting_it()
    {
        var act = () => DeliverySettings.Create(50m, null, codEnabled: false, codExtraFee: 25m, 1, 3, 7);

        act.Should().NotThrow();
    }

    // -----------------------------------------------------------------------
    // EffectiveCodFee is the only value pricing may read
    // -----------------------------------------------------------------------

    [Fact]
    public void EffectiveCodFee_is_zero_even_when_a_fee_is_stored_and_COD_is_off()
    {
        // Build an enabled-COD instance so a fee is definitely stored, then switch COD off.
        // The assertion cannot pass by accident on a fee that was never set.
        var delivery = DeliverySettings.Create(50m, null, codEnabled: true, codExtraFee: 30m, 1, 3, 7)
            .WithCodEnabled(false);

        delivery.CodExtraFee.Should().Be(30m);
        delivery.EffectiveCodFee.Should().Be(0m);
        delivery.IsCodAvailable.Should().BeFalse();
    }

    [Fact]
    public void Default_delivery_settings_have_no_COD_and_no_fee()
    {
        var delivery = DeliverySettings.Default();

        delivery.IsCodAvailable.Should().BeFalse();
        delivery.EffectiveCodFee.Should().Be(0m);
    }

    // -----------------------------------------------------------------------
    // WithCodEnabled — the integrations COD toggle
    // -----------------------------------------------------------------------

    /// <summary>
    /// Toggling COD from the integrations screen must not reset the fee, the flat shipping
    /// fee, the free-shipping threshold or the delivery-day estimates. Losing the day
    /// estimates here would silently change what the storefront promises to customers.
    /// </summary>
    [Fact]
    public void WithCodEnabled_preserves_every_other_shipping_value()
    {
        var original = DeliverySettings.Create(
            flatFeeAmount: 75m,
            freeShippingThreshold: 1200m,
            codEnabled: true,
            codExtraFee: 40m,
            processingDays: 2,
            minDeliveryDays: 4,
            maxDeliveryDays: 9);

        var toggled = original.WithCodEnabled(false);

        toggled.FlatFeeAmount.Should().Be(original.FlatFeeAmount);
        toggled.FreeShippingThreshold.Should().Be(original.FreeShippingThreshold);
        toggled.ProcessingDays.Should().Be(original.ProcessingDays);
        toggled.MinDeliveryDays.Should().Be(original.MinDeliveryDays);
        toggled.MaxDeliveryDays.Should().Be(original.MaxDeliveryDays);
        toggled.IsCodAvailable.Should().BeFalse();
    }

    /// <summary>
    /// A round trip must restore the original configuration exactly, fee included. If it does
    /// not, a merchant who temporarily disables COD and later re-enables it would silently
    /// start charging nothing — the exact failure the toggle is meant to prevent.
    /// </summary>
    [Fact]
    public void Toggling_COD_off_and_on_restores_the_original_configuration()
    {
        var original = DeliverySettings.Create(75m, 1200m, codEnabled: true, codExtraFee: 40m, 2, 4, 9);

        var roundTripped = original.WithCodEnabled(false).WithCodEnabled(true);

        roundTripped.Should().Be(original);
        roundTripped.EffectiveCodFee.Should().Be(40m);
    }

    [Fact]
    public void WithCodEnabled_does_not_mutate_the_original_instance()
    {
        var original = DeliverySettings.Create(50m, null, codEnabled: true, codExtraFee: 20m, 1, 3, 7);

        original.WithCodEnabled(false);

        original.IsCodAvailable.Should().BeTrue();
        original.EffectiveCodFee.Should().Be(20m);
    }

    [Fact]
    public void WithCodEnabled_to_the_same_value_is_a_no_op()
    {
        var original = DeliverySettings.Create(50m, null, codEnabled: true, codExtraFee: 20m, 1, 3, 7);

        original.WithCodEnabled(true).Should().Be(original);
    }

    // -----------------------------------------------------------------------
    // BusinessSettings.SetCodEnabled
    // -----------------------------------------------------------------------

    [Fact]
    public void SetCodEnabled_toggles_only_the_availability_flag()
    {
        var settings = BusinessSettings.CreateDefault("My Store");
        settings.UpdateDelivery(
            DeliverySettings.Create(60m, 900m, codEnabled: false, codExtraFee: 0m, 1, 3, 6));

        settings.SetCodEnabled(true);

        settings.Delivery.IsCodAvailable.Should().BeTrue();
        settings.Delivery.FlatFeeAmount.Should().Be(60m);
        settings.Delivery.FreeShippingThreshold.Should().Be(900m);
        settings.Delivery.MinDeliveryDays.Should().Be(3);
        settings.Delivery.MaxDeliveryDays.Should().Be(6);
    }

    [Fact]
    public void SetCodEnabled_false_disables_COD_on_the_business_settings()
    {
        var settings = BusinessSettings.CreateDefault("My Store");
        settings.SetCodEnabled(true);
        settings.Delivery.IsCodAvailable.Should().BeTrue();

        settings.SetCodEnabled(false);

        settings.Delivery.IsCodAvailable.Should().BeFalse();
        settings.Delivery.EffectiveCodFee.Should().Be(0m);
    }

    /// <summary>
    /// COD must not exist anywhere on BusinessSettings other than inside Delivery. A second
    /// copy of the flag is what allowed the two admin screens to disagree about availability.
    /// </summary>
    [Fact]
    public void COD_is_not_stored_outside_the_delivery_value_object()
    {
        // Matches on a whole word so that unrelated names such as CountryCode are not
        // mistaken for COD state.
        var codProperties = typeof(BusinessSettings)
            .GetProperties()
            .Select(p => p.Name)
            .Where(n => n.Split('_', StringSplitOptions.RemoveEmptyEntries)
                         .Any(part => part.Equals("Cod", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        codProperties.Should().BeEmpty(
            "cash-on-delivery state belongs solely to BusinessSettings.Delivery");
    }
}
