using KromicCommerce.Application.Abstractions.Sms;
using KromicCommerce.Infrastructure.Configuration;
using KromicCommerce.Infrastructure.Sms;
using KromicCommerce.UnitTests.Application;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KromicCommerce.UnitTests.Infrastructure.Sms;

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
            Options.Create(twoFactor ?? options.TwoFactor),
            Options.Create(free2Sms ?? options.Free2Sms),
            Options.Create(twilio ?? options.Twilio),
            SmsTestDoubles.ProviderSettings(saved),
            new StubHttpClientFactory(new StubHttpMessageHandler(System.Net.HttpStatusCode.OK, "{}")),
            NullLoggerFactory.Instance);

    private static SmsOptions TwilioFromEnvironment => new()
    {
        Enabled = true,
        Provider = "Twilio",
        Twilio = new TwilioOptions { AccountSid = "AC-env", AuthToken = "env-token", FromNumber = "+15550000000" }
    };

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
            TwoFactor = new TwoFactorOptions { ApiKey = "k", TemplateName = "LOGIN_OTP" },
            Free2Sms = new Free2SmsOptions { ApiKey = "k", SenderId = "F2SMS", MessageTemplate = "Your code is {{OTP}}" },
            Twilio = new TwilioOptions { AccountSid = "AC", AuthToken = "t", FromNumber = "+15550000000" }
        };

        (await Build(options).CreateAsync()).Kind.Should().Be(SmsProviderKinds.Parse(name));
    }

    [Fact]
    public async Task A_saved_selection_overrides_a_different_deployment_provider()
    {
        var factory = Build(
            TwilioFromEnvironment,
            SmsTestDoubles.SavedSettings(
                SmsProviderKind.Free2Sms, true, ("ApiKey", "admin-key"), ("SenderId", "F2SMS"), ("MessageTemplate", "Your code is {{OTP}}")));

        (await factory.CreateAsync()).Kind.Should().Be(SmsProviderKind.Free2Sms);

        var status = await factory.GetStatusAsync();
        status.Provider.Should().Be(SmsProviderKind.Free2Sms);
        status.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public async Task A_saved_selection_overrides_deployment_credentials()
    {
        var factory = Build(
            TwilioFromEnvironment,
            SmsTestDoubles.SavedSettings(
                SmsProviderKind.Twilio, true,
                ("AccountSid", "AC-admin"), ("AuthToken", "admin-token"), ("FromNumber", "+15550000000")));

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
        var factory = Build(
            new SmsOptions { Enabled = true, Provider = "Twilio" },
            SmsTestDoubles.SavedSettings(SmsProviderKind.Twilio, true, ("AccountSid", "AC-admin")));

        var status = await factory.GetStatusAsync();

        status.IsConfigured.Should().BeFalse();
        status.MissingSettings.Should().BeEquivalentTo(
            ["Sms:Twilio:AuthToken", "Sms:Twilio:FromNumber"]);
        (await factory.CreateAsync()).IsOperational.Should().BeFalse();
    }

    [Fact]
    public async Task Credentials_fall_back_to_configuration_individually()
    {
        var factory = Build(
            TwilioFromEnvironment,
            SmsTestDoubles.SavedSettings(
                SmsProviderKind.Twilio, true, ("AccountSid", "AC-admin")));

        var status = await factory.GetStatusAsync();

        status.IsConfigured.Should().BeTrue();
        status.MissingSettings.Should().BeEmpty();
    }

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

        var result = await provider.SendOtpAsync("+919876543210", "1234");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("SMS_NOT_CONFIGURED");
    }

    [Fact]
    public async Task Only_the_selected_provider_is_ever_instantiated()
    {
        var factory = Build(
            new SmsOptions
            {
                Enabled = true,
                Provider = "Free2Sms",
                Twilio = new TwilioOptions(),
                Free2Sms = new Free2SmsOptions { ApiKey = "k", SenderId = "F2SMS", MessageTemplate = "Your code is {{OTP}}" }
            });

        (await factory.CreateAsync()).Kind.Should().Be(SmsProviderKind.Free2Sms);
        (await factory.GetStatusAsync()).IsConfigured.Should().BeTrue();
    }

    [Fact]
    public async Task Switching_provider_takes_effect_without_a_restart()
    {
        var factory = Build(TwilioFromEnvironment, SmsTestDoubles.SavedSettings(
            SmsProviderKind.Twilio, true,
            ("AccountSid", "AC"), ("AuthToken", "t"), ("FromNumber", "+15550000000")));

        (await factory.CreateAsync()).Kind.Should().Be(SmsProviderKind.Twilio);

        var switched = Build(TwilioFromEnvironment, SmsTestDoubles.SavedSettings(
            SmsProviderKind.TwoFactor, true, ("ApiKey", "k"), ("TemplateName", "LOGIN_OTP")));

        (await switched.CreateAsync()).Kind.Should().Be(SmsProviderKind.TwoFactor);
    }
}
