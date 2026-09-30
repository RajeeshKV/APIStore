using System.Reflection;
using KromicCommerce.Application.Abstractions.Store;
using KromicCommerce.Application.Features.Store.UpdateDeliverySettings;

namespace KromicCommerce.UnitTests.Application;

/// <summary>
/// COD must be reachable through exactly one configuration path.
///
/// COD is a shipping concern, so both its availability flag and its surcharge live in
/// DeliverySettings and are mutated together by UpdateDeliverySettings. A second command
/// dedicated to COD used to exist (UpdateCashOnDeliveryCommand, exposed at
/// PUT /admin/integrations/payment/cod).
///
/// Both surfaces wrote the same value object, so the stored data was never inconsistent — but
/// two places to change one switch is precisely what allowed the Integrations and Shipping admin
/// screens to disagree about whether COD was available. These tests lock the single path in
/// place so the duplicate cannot be reintroduced.
/// </summary>
public sealed class SingleCodConfigurationPathTests
{
    [Fact]
    public void No_dedicated_COD_command_exists()
    {
        var codCommands = ApplicationCommands()
            .Where(t => t.Name.Contains("CashOnDelivery", StringComparison.OrdinalIgnoreCase)
                     || t.Name.Contains("Cod", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Name)
            .ToList();

        codCommands.Should().BeEmpty(
            "COD must be mutated only via UpdateDeliverySettings; a second command re-creates " +
            "the two-surface problem this removed");
    }

    [Fact]
    public void No_dedicated_COD_handler_exists()
    {
        var codHandlers = ApplicationTypes()
            .Where(t => t.Name.EndsWith("Handler", StringComparison.Ordinal))
            .Where(t => t.Name.Contains("CashOnDelivery", StringComparison.OrdinalIgnoreCase)
                     || t.Name.Contains("Cod", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Name)
            .ToList();

        codHandlers.Should().BeEmpty(
            "the authoritative COD mutation is UpdateDeliverySettingsHandler");
    }

    /// <summary>
    /// The shipping command must carry BOTH COD fields. If it carried only the flag, enabling
    /// COD under Shipping would not set a fee, and there would be no single place to do so.
    /// </summary>
    [Fact]
    public void The_shipping_command_is_the_authoritative_COD_mutation()
    {
        var properties = typeof(UpdateDeliverySettingsCommand)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToList();

        properties.Should().Contain("CodEnabled");
        properties.Should().Contain("CodExtraFee");
    }

    /// <summary>
    /// The domain still exposes a focused toggle for callers that only need to flip
    /// availability. That is an intentional, additive convenience on the single value object —
    /// it is not a second configuration surface, because it is not reachable as its own endpoint.
    /// </summary>
    [Fact]
    public void A_lossless_COD_toggle_remains_available_on_the_settings_object()
    {
        var method = typeof(BusinessSettings).GetMethod(nameof(BusinessSettings.SetCodEnabled));

        method.Should().NotBeNull(
            "SetCodEnabled is the in-model toggle; it mutates the same Delivery value object");
        method!.GetParameters().Should().ContainSingle("it must only toggle availability");
    }

    /// <summary>
    /// Whitelists what may be configured from the Integrations screen. Cash-on-delivery is
    /// absent by design: it is a shipping setting, not a third-party integration. Catching a
    /// future COD command under an unexpected name is the point of listing the full set rather
    /// than grepping for "cod".
    /// </summary>
    [Fact]
    public void Integration_commands_cover_only_real_third_party_integrations()
    {
        var integrationCommands = ApplicationCommands()
            .Where(t => t.Namespace?.Contains("Integrations", StringComparison.Ordinal) == true)
            .Select(t => t.Name)
            .OrderBy(n => n)
            .ToList();

        integrationCommands.Should().BeEquivalentTo(new[]
        {
            "UpdateEmailConfigCommand",
            "UpdateGoogleOAuthConfigCommand",
            "UpdateRazorpayConfigCommand",
            "UpdateSmsConfigCommand",
        },
        "cash on delivery is a shipping setting and must not be configurable from Integrations");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static IEnumerable<Type> ApplicationTypes()
        => typeof(IBusinessSettingsService).Assembly.GetTypes();

    /// <summary>
    /// Command types only. Handlers are excluded by name because ICommandHandler&lt;T&gt; also
    /// begins with "ICommand" and would otherwise match the interface check below.
    /// </summary>
    private static IEnumerable<Type> ApplicationCommands()
        => ApplicationTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => t.Name.EndsWith("Command", StringComparison.Ordinal))
            .Where(t => t.GetInterfaces().Any(i => i.Name.StartsWith("ICommand", StringComparison.Ordinal)))
            .Where(t => !t.Namespace!.StartsWith("Abstractions", StringComparison.Ordinal));
}
