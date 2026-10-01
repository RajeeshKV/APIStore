using KromicCommerce.Application.Features.Admin.Integrations;

namespace KromicCommerce.UnitTests.Application;

public sealed class UpdateSmsConfigValidatorTests
{
    private static UpdateSmsConfigValidator Validator => new();

    private static UpdateSmsConfigCommand Command(
        bool enabled = true,
        string provider = "Twilio",
        Dictionary<string, string>? settings = null)
        => new(enabled, provider, settings);

    private static bool IsValid(UpdateSmsConfigCommand command)
        => Validator.Validate(command).IsValid;

    [Theory]
    [InlineData("2Factor")]
    [InlineData("Free2SMS")]
    [InlineData("twilio")]
    [InlineData("  Twilio  ")]
    public void Accepts_each_supported_provider_with_its_required_settings(string provider)
    {
        var settings = provider.ToLowerInvariant() switch
        {
            "2factor" => new Dictionary<string, string> { ["ApiKey"] = "k" },
            "free2sms" => new Dictionary<string, string> { ["ApiKey"] = "k", ["SenderId"] = "F2SMS" },
            _ => new Dictionary<string, string> { ["AccountSid"] = "AC", ["AuthToken"] = "t", ["ServiceSid"] = "VA" }
        };

        IsValid(Command(provider: provider, settings: settings)).Should().BeTrue();
    }

    [Theory]
    [InlineData("TechTo")]
    [InlineData("SmsLocal")]
    [InlineData("Fast2SMS")]
    [InlineData("MSG91")]
    [InlineData("")]
    public void Rejects_a_removed_or_unknown_provider(string provider)
    {
        var result = Validator.Validate(Command(
            provider: provider,
            settings: new Dictionary<string, string> { ["ApiKey"] = "k" }));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Provider");
    }

    [Fact]
    public void Rejects_a_setting_that_does_not_belong_to_the_selected_provider()
    {
        // AuthToken is a Twilio setting; it means nothing to 2Factor.
        var result = Validator.Validate(Command(
            provider: "2Factor",
            settings: new Dictionary<string, string> { ["ApiKey"] = "k", ["AuthToken"] = "t" }));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ProviderSettings");
    }

    [Fact]
    public void Rejects_a_misspelled_setting_name()
    {
        // The failure this prevents: the save reported success, the key was never read, and
        // every send failed on missing credentials.
        var result = Validator.Validate(Command(
            provider: "Free2SMS",
            settings: new Dictionary<string, string> { ["ApiKey"] = "k", ["Senderid"] = "" }));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Accepts_setting_names_regardless_of_case()
        => IsValid(Command(
            provider: "Free2SMS",
            settings: new Dictionary<string, string> { ["apikey"] = "k", ["senderid"] = "F2SMS" }))
            .Should().BeTrue();

    [Fact]
    public void Requires_every_mandatory_setting_when_sms_is_enabled()
    {
        var result = Validator.Validate(Command(
            provider: "Twilio",
            settings: new Dictionary<string, string> { ["AccountSid"] = "AC" }));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Disabling_sms_does_not_require_credentials_that_were_never_saved()
    {
        IsValid(Command(enabled: false, provider: "Twilio", settings: null)).Should().BeTrue();
    }

    [Fact]
    public void Disabling_sms_still_rejects_an_unknown_setting_name()
    {
        // Otherwise a removed provider's leftover keys would be silently re-stored.
        IsValid(Command(
            enabled: false, provider: "Twilio",
            settings: new Dictionary<string, string> { ["Route"] = "4" })).Should().BeFalse();
    }

    [Fact]
    public void Rejects_an_oversized_value()
    {
        IsValid(Command(
            provider: "Free2SMS",
            settings: new Dictionary<string, string>
            {
                ["ApiKey"] = new string('k', 2001),
                ["SenderId"] = "F2SMS"
            })).Should().BeFalse();
    }
}

public sealed class SmsProviderKindsTests
{
    [Theory]
    [InlineData("2factor", SmsProviderKind.TwoFactor)]
    [InlineData("2 FACTOR", SmsProviderKind.TwoFactor)]
    [InlineData("2-factor", SmsProviderKind.TwoFactor)]
    [InlineData("free_2sms", SmsProviderKind.Free2Sms)]
    [InlineData("TWILIO", SmsProviderKind.Twilio)]
    [InlineData("TwilioVerify", SmsProviderKind.Twilio)]
    [InlineData("none", SmsProviderKind.None)]
    public void Recognises_known_spellings(string name, SmsProviderKind expected)
        => SmsProviderKinds.Parse(name).Should().Be(expected);

    [Theory]
    [InlineData("techto")]
    [InlineData("smslocal")]
    [InlineData("fast2sms")]
    [InlineData("msg91")]
    [InlineData(null)]
    [InlineData("   ")]
    public void Returns_null_for_anything_else(string? name)
        => SmsProviderKinds.Parse(name).Should().BeNull();

    [Fact]
    public void The_selectable_set_excludes_none()
    {
        SmsProviderKinds.Selectable.Should().NotContain(SmsProviderKind.None);
        SmsProviderKinds.Selectable.Should().HaveCount(3);
    }

    [Theory]
    [InlineData(SmsProviderKind.TwoFactor, "2Factor")]
    [InlineData(SmsProviderKind.Free2Sms, "Free2SMS")]
    [InlineData(SmsProviderKind.Twilio, "Twilio")]
    [InlineData(SmsProviderKind.None, "None")]
    public void Round_trips_through_the_canonical_name(SmsProviderKind kind, string name)
    {
        kind.ToName().Should().Be(name);
        SmsProviderKinds.Parse(name).Should().Be(kind);
    }

    [Fact]
    public void Only_the_three_gateways_are_selectable()
    {
        SmsProviderKind.TwoFactor.IsSelectable().Should().BeTrue();
        SmsProviderKind.Free2Sms.IsSelectable().Should().BeTrue();
        SmsProviderKind.Twilio.IsSelectable().Should().BeTrue();
        SmsProviderKind.None.IsSelectable().Should().BeFalse();
    }
}
