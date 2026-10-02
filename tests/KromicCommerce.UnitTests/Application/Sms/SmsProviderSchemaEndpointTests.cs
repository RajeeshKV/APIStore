using KromicCommerce.Application.Abstractions.Sms;
using KromicCommerce.Application.Features.Admin.SmsTemplates;
using KromicCommerce.Domain.Sms;

namespace KromicCommerce.UnitTests.Application.Sms;

/// <summary>
/// The admin provider list is what a front end renders its configuration form from, so these
/// tests pin the shape of that response rather than the schema internals.
/// </summary>
public sealed class GetSmsProvidersHandlerTests
{
    private static async Task<IReadOnlyList<Contracts.Admin.SmsProviderOptionResponse>> ListAsync()
    {
        var result = await new GetSmsProvidersHandler()
            .Handle(new GetSmsProvidersQuery(), CancellationToken.None);

        return result.Value;
    }

    [Fact]
    public async Task Returns_every_selectable_gateway_with_a_populated_schema()
    {
        var providers = await ListAsync();

        // Guards a real regression: All was once an eagerly initialised static field declared
        // above the individual schemas, so it captured an array of nulls and this endpoint
        // answered with providers whose name was null.
        providers.Should().HaveCount(3);
        providers.Should().OnlyContain(p => p.Name != null);
        providers.Select(p => p.Name).Should()
            .BeEquivalentTo(["2Factor", "Free2SMS", "Twilio"]);
        providers.Should().OnlyContain(p => p.Description != null);
    }

    [Fact]
    public async Task TwoFactor_advertises_the_inputs_the_user_actually_needs_to_enter()
    {
        var twoFactor = (await ListAsync()).Single(p => p.Name == "2Factor");

        // The admin-facing names, not the wire keys: this is what the form labels.
        twoFactor.Settings.Select(f => f.Label).Should().Contain(["API key", "Sender ID"]);
        twoFactor.TemplateFields.Select(f => f.Label).Should().Contain("Template name");

        var apiKey = twoFactor.Settings.Single(f => f.Key == "ApiKey");
        apiKey.Required.Should().BeTrue();
        apiKey.Type.Should().Be(Contracts.Admin.SmsFieldType.Secret);

        var templateName = twoFactor.TemplateFields.Single(f => f.Key == "externalTemplateId");
        templateName.Required.Should().BeTrue();
        templateName.Placeholder.Should().Be("LOGIN_OTP");
    }

    [Fact]
    public async Task Free2Sms_shows_no_template_name_because_it_does_not_use_one()
    {
        var free2Sms = (await ListAsync()).Single(p => p.Name == "Free2SMS");

        // Only the API key and sender ID at the top level, which is exactly the "hide what does
        // not apply" rule.
        free2Sms.Settings.Where(f => !f.Advanced)
            .Select(f => f.Key).Should().BeEquivalentTo(["ApiKey", "SenderId"]);
        free2Sms.TemplateFields.Select(f => f.Label).Should().Contain("DLT template ID");
        free2Sms.TemplateFields.Should().NotContain(f => f.Label == "Template name");
        free2Sms.SupportsNativeOtp.Should().BeFalse();
    }

    [Fact]
    public async Task Twilio_shows_a_template_sid_rather_than_a_template_name()
    {
        var twilio = (await ListAsync()).Single(p => p.Name == "Twilio");

        twilio.TemplateFields.Select(f => f.Label).Should().Contain("Verify Template SID");
        twilio.RequiredSettings.Should()
            .BeEquivalentTo(["AccountSid", "AuthToken", "ServiceSid"]);
    }

    [Fact]
    public async Task Every_provider_explains_the_conditions_on_its_fields()
    {
        foreach (var provider in await ListAsync())
        {
            provider.Notes.Should().NotBeEmpty();
            provider.Settings.Should().NotBeEmpty();
        }
    }
}

/// <summary>
/// Switching provider must not leave the previous gateway's values behind. A stored 2Factor
/// OtpPath is meaningless to Free2Sms and would otherwise be resurrected if the admin switched
/// back.
/// </summary>
public sealed class SmsProviderSwitchClearsInapplicableSettingsTests
{
    [Fact]
    public void Switching_provider_drops_settings_that_do_not_apply_to_the_new_one()
    {
        var twoFactorSettings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ApiKey"] = "2fa-secret",
            ["OtpPath"] = "/API/V1/OTP/SEND",
            ["TemplateNameField"] = "template_name"
        };

        var config = SmsProviderConfig.Create(true, SmsProviderKind.TwoFactor, twoFactorSettings);
        config.EncryptedSettings.Should().Contain("OtpPath");

        // Free2Sms accepts none of the 2Factor route knobs, so its validator rejects them and the
        // admin form only ever submits its own keys.
        var free2SmsSettings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ApiKey"] = "f2sms-key",
            ["SenderId"] = "F2SMS"
        };

        config.Configure(true, SmsProviderKind.Free2Sms, free2SmsSettings);

        config.Kind.Should().Be(SmsProviderKind.Free2Sms);
        config.EncryptedSettings.Should().NotContain("OtpPath");
        config.EncryptedSettings.Should().NotContain("TemplateNameField");
        config.EncryptedSettings.Should().Contain("SenderId");
    }

    [Fact]
    public void Removing_a_key_from_the_form_removes_the_stored_value()
    {
        // Configure replaces rather than merges, so an admin clearing a field genuinely clears it.
        var config = SmsProviderConfig.Create(
            true, SmsProviderKind.TwoFactor,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ApiKey"] = "2fa-secret",
                ["SenderId"] = "STORE"
            });

        config.Configure(true, SmsProviderKind.TwoFactor,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["ApiKey"] = "2fa-secret" });

        config.EncryptedSettings.Should().NotContain("SenderId");
    }
}
