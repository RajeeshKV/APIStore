namespace KromicCommerce.UnitTests.Application;

public sealed class SmsOptionsTests
{
    private static SmsOptions Fully(SmsProviderKind provider) => provider switch
    {
        SmsProviderKind.TwoFactor => new SmsOptions
        {
            Enabled = true,
            Provider = "2Factor",
            TwoFactor = new TwoFactorOptions { ApiKey = "k" }
        },
        SmsProviderKind.Free2Sms => new SmsOptions
        {
            Enabled = true,
            Provider = "Free2Sms",
            Free2Sms = new Free2SmsOptions { ApiKey = "k", SenderId = "F2SMS" }
        },
        SmsProviderKind.Twilio => new SmsOptions
        {
            Enabled = true,
            Provider = "Twilio",
            Twilio = new TwilioOptions { AccountSid = "AC", AuthToken = "t", ServiceSid = "VA" }
        },
        _ => new SmsOptions()
    };

    [Theory]
    [InlineData("2Factor", SmsProviderKind.TwoFactor)]
    [InlineData("2factor", SmsProviderKind.TwoFactor)]
    [InlineData("  2 Factor  ", SmsProviderKind.TwoFactor)]
    [InlineData("Free2SMS", SmsProviderKind.Free2Sms)]
    [InlineData("free2sms", SmsProviderKind.Free2Sms)]
    [InlineData("Twilio", SmsProviderKind.Twilio)]
    [InlineData("twilio-verify", SmsProviderKind.Twilio)]
    public void Provider_name_parsing_is_case_and_whitespace_insensitive(string name, SmsProviderKind expected)
        => new SmsOptions { Provider = name }.SelectedProvider.Should().Be(expected);

    [Theory]
    [InlineData("TechTo")]
    [InlineData("SmsLocal")]
    [InlineData("Fast2SMS")]
    [InlineData("DefinitelyNotAProvider")]
    public void Removed_and_unrecognised_provider_names_degrade_to_None_rather_than_throwing(string name)
        => new SmsOptions { Provider = name }.SelectedProvider.Should().Be(SmsProviderKind.None);

    [Fact]
    public void An_unrecognised_provider_degrades_to_None_rather_than_throwing()
    {
        var options = new SmsOptions { Enabled = true, Provider = "DefinitelyNotAProvider" };

        options.SelectedProvider.Should().Be(SmsProviderKind.None);
        options.IsConfigured.Should().BeFalse();
        options.GetMissingSettings().Should().Contain("Sms:Provider");
    }

    [Theory]
    [InlineData(SmsProviderKind.TwoFactor)]
    [InlineData(SmsProviderKind.Free2Sms)]
    [InlineData(SmsProviderKind.Twilio)]
    [InlineData(SmsProviderKind.None)]
    public void A_complete_selection_is_configured(SmsProviderKind provider)
        => Fully(provider).IsConfigured.Should().Be(provider != SmsProviderKind.None);

    [Fact]
    public void Only_the_three_supported_providers_are_selectable()
        => SmsProviderKinds.Selectable.Should().BeEquivalentTo(
        [
            SmsProviderKind.TwoFactor,
            SmsProviderKind.Free2Sms,
            SmsProviderKind.Twilio
        ]);

    [Fact]
    public void Missing_credentials_are_reported_per_provider()
    {
        var options = new SmsOptions
        {
            Enabled = true,
            Provider = "Twilio",
            Twilio = new TwilioOptions { AccountSid = "AC", AuthToken = "t" } // ServiceSid omitted
        };

        options.IsConfigured.Should().BeFalse();
        options.GetMissingSettings().Should().BeEquivalentTo(["Sms:Twilio:ServiceSid"]);
    }

    [Fact]
    public void TwoFactor_only_requires_an_api_key()
    {
        var options = new SmsOptions
        {
            Enabled = true,
            Provider = "2Factor",
            TwoFactor = new TwoFactorOptions { ApiKey = "k" }
        };

        options.IsConfigured.Should().BeTrue();
        options.GetMissingSettings().Should().BeEmpty();
    }

    [Fact]
    public void Disabled_SMS_reports_the_master_switch_as_missing()
    {
        var options = new SmsOptions { Enabled = false, Provider = "Twilio" };

        options.IsConfigured.Should().BeFalse();
        options.GetMissingSettings().Should().BeEquivalentTo(["Sms:Enabled"]);
    }

    [Fact]
    public void Credentials_for_inactive_providers_are_ignored()
    {
        // Twilio is selected and complete; Free2Sms has nothing. Only the selection matters.
        var options = Fully(SmsProviderKind.Twilio);

        options.IsConfigured.Should().BeTrue();
        options.GetMissingSettings().Should().BeEmpty();
    }

    [Fact]
    public void Out_of_range_OTP_policy_is_clamped()
    {
        var policy = new SmsOtpPolicyOptions
        {
            ExpiryMinutes = 999,
            ResendCooldownSeconds = -5,
            MaxAttempts = 0,
            Length = 3
        };

        policy.ClampedExpiryMinutes.Should().Be(60);
        policy.ClampedResendCooldownSeconds.Should().Be(0);
        policy.ClampedMaxAttempts.Should().Be(1);
        policy.ClampedLength.Should().Be(4);
    }
}
