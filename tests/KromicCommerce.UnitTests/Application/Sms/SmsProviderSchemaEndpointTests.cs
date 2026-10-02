using KromicCommerce.Application.Features.Admin.SmsTemplates;
using KromicCommerce.Contracts.Admin;
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

        providers.Should().HaveCount(3);
        providers.Should().OnlyContain(p => p.Name != null);
        providers.Select(p => p.Name).Should()
            .BeEquivalentTo(["2Factor", "Free2SMS", "Twilio"]);
        providers.Should().OnlyContain(p => p.Description != null);
    }

    [Fact]
    public async Task TwoFactor_advertises_api_key_and_template_name()
    {
        var twoFactor = (await ListAsync()).Single(p => p.Name == "2Factor");

        twoFactor.Settings.Select(f => f.Key).Should()
            .BeEquivalentTo(["ApiKey", "TemplateName"]);
        twoFactor.RequiredSettings.Should().BeEquivalentTo(["ApiKey", "TemplateName"]);

        var apiKey = twoFactor.Settings.Single(f => f.Key == "ApiKey");
        apiKey.Required.Should().BeTrue();
        apiKey.Type.Should().Be(Contracts.Admin.SmsFieldType.Secret);

        var templateName = twoFactor.Settings.Single(f => f.Key == "TemplateName");
        templateName.Required.Should().BeTrue();
        templateName.Type.Should().Be(Contracts.Admin.SmsFieldType.Text);
    }

    [Fact]
    public async Task Free2Sms_advertises_api_key_sender_id_and_message_template()
    {
        var free2Sms = (await ListAsync()).Single(p => p.Name == "Free2SMS");

        free2Sms.Settings.Select(f => f.Key).Should()
            .BeEquivalentTo(["ApiKey", "SenderId", "MessageTemplate"]);

        var template = free2Sms.Settings.Single(f => f.Key == "MessageTemplate");
        template.Type.Should().Be(Contracts.Admin.SmsFieldType.Textarea);
    }

    [Fact]
    public async Task Twilio_advertises_account_sid_auth_token_and_from_number()
    {
        var twilio = (await ListAsync()).Single(p => p.Name == "Twilio");

        twilio.Settings.Select(f => f.Key).Should()
            .BeEquivalentTo(["AccountSid", "AuthToken", "FromNumber"]);
        twilio.RequiredSettings.Should()
            .BeEquivalentTo(["AccountSid", "AuthToken", "FromNumber"]);

        var authToken = twilio.Settings.Single(f => f.Key == "AuthToken");
        authToken.Type.Should().Be(Contracts.Admin.SmsFieldType.Secret);
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

    [Fact]
    public async Task No_provider_advertises_delivery_mode_or_template_fields()
    {
        var providers = await ListAsync();

        foreach (var provider in providers)
        {
            provider.Settings.Should().NotContain(f => f.Key == "DeliveryMode");
            provider.Settings.Should().OnlyContain(f => f.Type != Contracts.Admin.SmsFieldType.Select);
        }
    }
}

/// <summary>
/// Switching provider must not leave the previous gateway's values behind.
/// </summary>
public sealed class SmsProviderSwitchClearsInapplicableSettingsTests
{
    [Fact]
    public void Switching_provider_drops_settings_that_do_not_apply_to_the_new_one()
    {
        var twoFactorSettings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ApiKey"] = "2fa-secret",
            ["TemplateName"] = "LOGIN_OTP"
        };

        var config = SmsProviderConfig.Create(true, SmsProviderKind.TwoFactor, twoFactorSettings);
        config.EncryptedSettings.Should().Contain("TemplateName");

        var free2SmsSettings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ApiKey"] = "f2sms-key",
            ["SenderId"] = "F2SMS",
            ["MessageTemplate"] = "Your code is {{OTP}}"
        };

        config.Configure(true, SmsProviderKind.Free2Sms, free2SmsSettings);

        config.Kind.Should().Be(SmsProviderKind.Free2Sms);
        config.EncryptedSettings.Should().NotContain("TemplateName");
        config.EncryptedSettings.Should().Contain("SenderId");
    }

    [Fact]
    public void Removing_a_key_from_the_form_removes_the_stored_value()
    {
        var config = SmsProviderConfig.Create(
            true, SmsProviderKind.TwoFactor,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ApiKey"] = "2fa-secret",
                ["TemplateName"] = "LOGIN_OTP"
            });

        config.Configure(true, SmsProviderKind.TwoFactor,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["ApiKey"] = "2fa-secret" });

        config.EncryptedSettings.Should().NotContain("TemplateName");
    }
}
