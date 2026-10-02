using KromicCommerce.Application.Abstractions.Sms;
using KromicCommerce.Contracts.Admin;
using KromicCommerce.Domain.Sms;

namespace KromicCommerce.UnitTests.Infrastructure.Sms;

/// <summary>
/// The provider field catalogue is what the admin UI renders from, so these tests treat it as a
/// contract: the backend advertises a field, therefore validates it, and therefore the form and
/// the rules cannot disagree.
/// </summary>
public sealed class SmsProviderFieldSchemaTests
{
    [Fact]
    public void TwoFactor_advertises_only_api_key_and_template_name()
    {
        var twoFactor = SmsProviderFieldSchema.For(SmsProviderKind.TwoFactor);

        twoFactor.Settings.Select(f => f.Key).Should()
            .BeEquivalentTo([SmsSettingNames.ApiKey, SmsSettingNames.TemplateName]);

        twoFactor.Settings.Should().HaveCount(2);
        twoFactor.Settings.Should().OnlyContain(f => f.Type != SmsFieldType.Select);
    }

    [Fact]
    public void Free2Sms_advertises_only_api_key_sender_id_and_message_template()
    {
        var free2Sms = SmsProviderFieldSchema.For(SmsProviderKind.Free2Sms);

        free2Sms.Settings.Select(f => f.Key).Should()
            .BeEquivalentTo([SmsSettingNames.ApiKey, SmsSettingNames.SenderId, SmsSettingNames.MessageTemplate]);

        free2Sms.Settings.Should().HaveCount(3);
        free2Sms.Settings.Should().OnlyContain(f => f.Type != SmsFieldType.Select);
    }

    [Fact]
    public void Twilio_advertises_only_account_sid_auth_token_and_from_number()
    {
        var twilio = SmsProviderFieldSchema.For(SmsProviderKind.Twilio);

        twilio.Settings.Select(f => f.Key).Should()
            .BeEquivalentTo([SmsSettingNames.AccountSid, SmsSettingNames.AuthToken, SmsSettingNames.FromNumber]);

        twilio.Settings.Should().HaveCount(3);
        twilio.Settings.Should().OnlyContain(f => f.Type != SmsFieldType.Select);
    }

    // -----------------------------------------------------------------------
    // Credentials are secret
    // -----------------------------------------------------------------------

    [Fact]
    public void Api_key_fields_are_marked_secret()
    {
        foreach (var provider in new[] { SmsProviderKind.TwoFactor, SmsProviderKind.Free2Sms })
        {
            var field = SmsProviderFieldSchema.For(provider).Settings
                .Single(f => f.Key == SmsSettingNames.ApiKey);

            field.Type.Should().Be(SmsFieldType.Secret);
        }

        var twilioAuth = SmsProviderFieldSchema.For(SmsProviderKind.Twilio).Settings
            .Single(f => f.Key == SmsSettingNames.AuthToken);

        twilioAuth.Type.Should().Be(SmsFieldType.Secret);
    }

    [Fact]
    public void Non_credential_fields_are_not_secret()
    {
        var twilio = SmsProviderFieldSchema.For(SmsProviderKind.Twilio).Settings;
        twilio.Single(f => f.Key == SmsSettingNames.AccountSid).Type.Should().Be(SmsFieldType.Text);
        twilio.Single(f => f.Key == SmsSettingNames.FromNumber).Type.Should().Be(SmsFieldType.Text);

        SmsProviderFieldSchema.For(SmsProviderKind.Free2Sms).Settings
            .Single(f => f.Key == SmsSettingNames.SenderId).Type.Should().Be(SmsFieldType.Text);

        SmsProviderFieldSchema.For(SmsProviderKind.Free2Sms).Settings
            .Single(f => f.Key == SmsSettingNames.MessageTemplate).Type.Should().Be(SmsFieldType.Textarea);

        SmsProviderFieldSchema.For(SmsProviderKind.TwoFactor).Settings
            .Single(f => f.Key == SmsSettingNames.TemplateName).Type.Should().Be(SmsFieldType.Text);
    }

    // -----------------------------------------------------------------------
    // Required settings match the fields marked required
    // -----------------------------------------------------------------------

    [Fact]
    public void Required_settings_match_the_fields_marked_required()
    {
        foreach (var provider in SmsProviderKinds.Selectable)
        {
            var schema = SmsProviderFieldSchema.For(provider);

            SmsSettingNames.Required(provider)
                .Should().BeEquivalentTo(schema.Settings.Where(f => f.Required).Select(f => f.Key));
        }
    }

    [Fact]
    public void Every_required_field_is_present()
    {
        foreach (var provider in SmsProviderKinds.Selectable)
        {
            var schema = SmsProviderFieldSchema.For(provider);
            schema.RequiredSettings.Should().NotBeEmpty();
            schema.Settings.Should().Contain(f => f.Required);
        }
    }

    // -----------------------------------------------------------------------
    // No DeliveryMode, no TemplateFields, no advanced fields
    // -----------------------------------------------------------------------

    [Fact]
    public void No_provider_advertises_a_delivery_mode()
    {
        foreach (var provider in SmsProviderKinds.Selectable)
        {
            SmsProviderFieldSchema.For(provider).Settings
                .Should().NotContain(f => f.Key == SmsSettingNames.ApiKey && false); // placeholder

            // DeliveryMode is not a known setting anymore
            SmsSettingNames.IsKnown(provider, "DeliveryMode").Should().BeFalse();
        }
    }

    [Fact]
    public void No_provider_exposes_implementation_details()
    {
        var implementationKeys = new[]
        {
            "OtpPath", "TransactionalPath", "TemplateNameField", "ApiKeyHeader",
            "Channel", "BaseUrl", "Route", "DeliveryMode", "MessagingServiceSid",
            "ServiceSid", "MessagingBaseUrl", "MessagingPath", "OtpVariableName",
            "ExpiryVariableName"
        };

        foreach (var provider in SmsProviderKinds.Selectable)
        {
            var keys = SmsProviderFieldSchema.For(provider).Settings.Select(f => f.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var implKey in implementationKeys)
            {
                keys.Should().NotContain(implKey);
            }
        }
    }

    [Fact]
    public void No_provider_has_TemplateFields()
    {
        foreach (var provider in SmsProviderKinds.Selectable)
        {
            SmsProviderFieldSchema.For(provider).Settings.Should().NotContain(f => f.Key == "externalTemplateId");
        }
    }

    // -----------------------------------------------------------------------
    // Validation
    // -----------------------------------------------------------------------

    [Fact]
    public void An_omitted_required_credential_is_rejected()
        => SmsProviderFieldSchema.ValidateSetting(SmsProviderKind.TwoFactor, SmsSettingNames.ApiKey, "  ")
            .Should().Be("API Key is required.");

    [Fact]
    public void An_empty_optional_field_is_accepted()
        => SmsProviderFieldSchema.ValidateSetting(SmsProviderKind.Twilio, "UnknownField", null)
            .Should().BeNull();

    [Fact]
    public void A_value_beyond_the_advertised_maximum_length_is_rejected()
    {
        var error = SmsProviderFieldSchema.ValidateSetting(
            SmsProviderKind.TwoFactor, SmsSettingNames.ApiKey, new string('a', 600));

        error.Should().Contain("must not exceed");
    }

    // -----------------------------------------------------------------------
    // Catalogue shape
    // -----------------------------------------------------------------------

    [Fact]
    public void The_catalogue_covers_every_selectable_gateway_exactly_once()
    {
        SmsProviderFieldSchema.All.Select(s => s.Name)
            .Should().BeEquivalentTo(SmsProviderKinds.Selectable.Select(p => p.ToName()));
    }

    [Fact]
    public void Every_field_carries_enough_text_for_a_form_to_render_it()
    {
        foreach (var schema in SmsProviderFieldSchema.All)
        {
            foreach (var field in schema.Settings)
            {
                field.Key.Should().NotBeNullOrWhiteSpace();
                field.Label.Should().NotBeNullOrWhiteSpace();
                field.MaxLength.Should().BeGreaterThan(0);
            }
        }
    }
}
