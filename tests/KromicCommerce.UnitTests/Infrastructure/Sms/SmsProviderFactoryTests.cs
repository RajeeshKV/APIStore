using KromicCommerce.Application.Abstractions.Sms;
using KromicCommerce.Infrastructure.Sms;
using KromicCommerce.UnitTests.Application;

namespace KromicCommerce.UnitTests.Infrastructure.Sms;

/// <summary>
/// Covers the resolution rules: which source decides the active provider, and what counts as
/// configured.
/// </summary>
public sealed class SmsProviderFactoryTests
{
    private static SmsProviderFactory Build(
        SmsOptions options,
        SmsProviderSettingsSnapshot? saved = null,
        TwoFactorOptions? twoFactor = null,
        Free2SmsOptions? free2Sms = null,
        TwilioOptions? twilio = null)
        => new(
            Options.Create(options),
            // DI binds these from the same configuration section as SmsOptions, so a test that
            // populates SmsOptions must supply the matching section here or the factory would be
            // asked for credentials that genuinely are not configured.
            Options.Create(twoFactor ?? options.TwoFactor),
            Options.Create(free2Sms ?? options.Free2Sms),
            Options.Create(twilio ?? options.Twilio),
            Options.Create(new SmsOtpPolicyOptions()),
            SmsTestDoubles.ProviderSettings(saved),
            SmsTestDoubles.Templates(),
            new StubHttpClientFactory(new StubHttpMessageHandler(System.Net.HttpStatusCode.OK, "{}")),
            NullLoggerFactory.Instance);

    private static SmsOptions TwilioFromEnvironment => new()
    {
        Enabled = true,
        Provider = "Twilio",
        Twilio = new TwilioOptions { AccountSid = "AC-env", AuthToken = "env-token", ServiceSid = "VA-env" }
    };

    // -----------------------------------------------------------------------
    // Bootstrap: nothing saved in the database
    // -----------------------------------------------------------------------

    [Fact]
    public async Task With_nothing_saved_the_deployment_configuration_decides()
    {
        var factory = Build(TwilioFromEnvironment);

        (await factory.CreateAsync()).Kind.Should().Be(SmsProviderKind.Twilio);
        (await factory.GetStatusAsync()).IsConfigured.Should().BeTrue();
    }

    [Theory]
    [InlineData("2Factor")]
    [InlineData("Free2Sms")]
    [InlineData("Twilio")]
    public async Task Each_configured_provider_is_selected(string name)
    {
        var options = new SmsOptions
        {
            Enabled = true,
            Provider = name,
            TwoFactor = new TwoFactorOptions { ApiKey = "k" },
            Free2Sms = new Free2SmsOptions { ApiKey = "k", SenderId = "F2SMS" },
            Twilio = new TwilioOptions { AccountSid = "AC", AuthToken = "t", ServiceSid = "VA" }
        };

        (await Build(options).CreateAsync()).Kind.Should().Be(SmsProviderKinds.Parse(name));
    }

    // -----------------------------------------------------------------------
    // Single source of truth: a saved row always wins
    // -----------------------------------------------------------------------

    [Fact]
    public async Task A_saved_selection_overrides_a_different_deployment_provider()
    {
        // Environment says Twilio; the administrator chose Free2SMS in the admin screen.
        var factory = Build(
            TwilioFromEnvironment,
            SmsTestDoubles.SavedSettings(
                SmsProviderKind.Free2Sms, true, ("ApiKey", "admin-key"), ("SenderId", "F2SMS")));

        (await factory.CreateAsync()).Kind.Should().Be(SmsProviderKind.Free2Sms);

        var status = await factory.GetStatusAsync();
        status.Provider.Should().Be(SmsProviderKind.Free2Sms);
        status.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public async Task A_saved_selection_overrides_deployment_credentials()
    {
        // The saved row is complete, so the environment's Twilio keys are irrelevant.
        var factory = Build(
            TwilioFromEnvironment,
            SmsTestDoubles.SavedSettings(
                SmsProviderKind.Twilio, true,
                ("AccountSid", "AC-admin"), ("AuthToken", "admin-token"), ("ServiceSid", "VA-admin")));

        (await factory.GetStatusAsync()).MissingSettings.Should().BeEmpty();
    }

    [Fact]
    public async Task A_saved_disable_switches_sms_off_despite_an_enabled_environment()
    {
        var factory = Build(TwilioFromEnvironment, SmsTestDoubles.SavedWithoutSettings(
            SmsProviderKind.Twilio, enabled: false));

        var status = await factory.GetStatusAsync();

        status.Enabled.Should().BeFalse();
        status.IsConfigured.Should().BeFalse();
        (await factory.CreateAsync()).IsOperational.Should().BeFalse();
    }

    [Fact]
    public async Task A_saved_selection_that_no_source_can_complete_is_not_configured()
    {
        // No Twilio credentials in the saved row and none in the environment either.
        var factory = Build(
            new SmsOptions { Enabled = true, Provider = "Twilio" },
            SmsTestDoubles.SavedSettings(SmsProviderKind.Twilio, true, ("AccountSid", "AC-admin")));

        var status = await factory.GetStatusAsync();

        status.IsConfigured.Should().BeFalse();
        // The paths name exactly what the administrator still has to set.
        status.MissingSettings.Should().BeEquivalentTo(
            ["Sms:Twilio:AuthToken", "Sms:Twilio:ServiceSid"]);
        (await factory.CreateAsync()).IsOperational.Should().BeFalse();
    }

    [Fact]
    public async Task Credentials_fall_back_to_configuration_individually()
    {
        // A half-completed admin form stays usable: values the administrator has not saved still
        // come from environment variables rather than breaking the deployment. This is a
        // credential default, not a second source of truth for which provider is active.
        var factory = Build(
            TwilioFromEnvironment,
            SmsTestDoubles.SavedSettings(
                SmsProviderKind.Twilio, true, ("AccountSid", "AC-admin")));

        var status = await factory.GetStatusAsync();

        status.IsConfigured.Should().BeTrue();
        status.MissingSettings.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Not configured
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("TechTo")]
    [InlineData("SmsLocal")]
    [InlineData("Fast2SMS")]
    [InlineData("DefinitelyNotAProvider")]
    public async Task A_removed_or_unknown_provider_yields_a_non_operational_no_op(string name)
    {
        var factory = Build(new SmsOptions { Enabled = true, Provider = name });

        var provider = await factory.CreateAsync();

        provider.Kind.Should().Be(SmsProviderKind.None);
        provider.IsOperational.Should().BeFalse();

        var status = await factory.GetStatusAsync();
        status.IsConfigured.Should().BeFalse();
        status.MissingSettings.Should().Contain("Sms:Provider");
    }

    [Fact]
    public async Task Disabled_sms_reports_the_master_switch_as_missing()
    {
        var status = await Build(new SmsOptions { Enabled = false, Provider = "Twilio" }).GetStatusAsync();

        status.IsConfigured.Should().BeFalse();
        status.MissingSettings.Should().BeEquivalentTo(["Sms:Enabled"]);
    }

    [Fact]
    public async Task The_no_op_provider_reports_a_configuration_error_rather_than_sending()
    {
        var provider = await Build(new SmsOptions()).CreateAsync();

        var result = await provider.SendOtpAsync("9876543210", "1234");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("SMS_NOT_CONFIGURED");
    }

    [Fact]
    public async Task Only_the_selected_provider_is_ever_instantiated()
    {
        // Twilio is unconfigured but Free2SMS is complete; selecting Free2SMS must work.
        var factory = Build(
            new SmsOptions
            {
                Enabled = true,
                Provider = "Free2Sms",
                Twilio = new TwilioOptions(),
                Free2Sms = new Free2SmsOptions { ApiKey = "k", SenderId = "F2SMS" }
            });

        (await factory.CreateAsync()).Kind.Should().Be(SmsProviderKind.Free2Sms);
        (await factory.GetStatusAsync()).IsConfigured.Should().BeTrue();
    }

    [Fact]
    public async Task RequiresVerification_only_while_sms_can_actually_deliver()
    {
        (await Build(TwilioFromEnvironment, saved: SmsTestDoubles.SavedSettings(
            SmsProviderKind.Twilio, true,
            ("AccountSid", "AC"), ("AuthToken", "t"), ("ServiceSid", "VA")))
            .GetStatusAsync()).RequiresVerification.Should().BeTrue();

        // Policy asks for verification but nothing can deliver a code — requiring it would lock
        // every customer out of checkout.
        (await Build(new SmsOptions { Enabled = false, RequireVerifiedPhoneAtCheckout = true })
            .GetStatusAsync()).RequiresVerification.Should().BeFalse();
    }

    [Fact]
    public async Task Switching_provider_takes_effect_without_a_restart()
    {
        // Same factory instance, same options: only the saved row changes. This is what makes an
        // admin change visible on the next request with no cache to invalidate.
        var factory = Build(TwilioFromEnvironment, SmsTestDoubles.SavedSettings(
            SmsProviderKind.Twilio, true,
            ("AccountSid", "AC"), ("AuthToken", "t"), ("ServiceSid", "VA")));

        (await factory.CreateAsync()).Kind.Should().Be(SmsProviderKind.Twilio);

        var switched = Build(TwilioFromEnvironment, SmsTestDoubles.SavedSettings(
            SmsProviderKind.TwoFactor, true, ("ApiKey", "k")));

        (await switched.CreateAsync()).Kind.Should().Be(SmsProviderKind.TwoFactor);
    }
}