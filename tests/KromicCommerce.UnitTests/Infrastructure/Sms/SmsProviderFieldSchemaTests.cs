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
    private static SmsProviderField Field(SmsProviderKind provider, string key) =>
        SmsProviderFieldSchema.For(provider).Settings
            .Single(f => f.Key == key);

    // -----------------------------------------------------------------------
    // Per-provider field selection — this is the "which inputs do I show" decision
    // -----------------------------------------------------------------------

    [Fact]
    public void TwoFactor_offers_the_template_name_because_its_identifier_is_a_name()
    {
        var reference = SmsProviderFieldSchema.TemplateReferenceField(SmsProviderKind.TwoFactor);

        reference.Should().NotBeNull();
        reference!.Label.Should().Be("Template name");
        reference.Required.Should().BeTrue();
        reference.Placeholder.Should().Be("LOGIN_OTP");
        // The label is the whole point: the same wire field is a "Template SID" for Twilio.
        reference.Label.Should().NotBe("Template ID");
    }

    [Fact]
    public void Twilio_labels_the_same_wire_field_as_a_template_sid()
    {
        var reference = SmsProviderFieldSchema.TemplateReferenceField(SmsProviderKind.Twilio);

        reference!.Label.Should().Be("Verify Template SID");
        reference.Placeholder.Should().StartWith("HJ");
        // Optional: Twilio falls back to the Service default, then the Verify default template.
        reference.Required.Should().BeFalse();
    }

    [Fact]
    public void Free2Sms_labels_the_same_wire_field_as_a_dlt_template_id()
    {
        var reference = SmsProviderFieldSchema.TemplateReferenceField(SmsProviderKind.Free2Sms);

        reference!.Label.Should().Be("DLT template ID");
        reference.Required.Should().BeFalse();
    }

    [Fact]
    public void Free2Sms_shows_only_the_api_key_and_sender_id_by_default()
    {
        // The advanced knobs exist but are flagged so a UI can collapse them.
        var free2Sms = SmsProviderFieldSchema.For(SmsProviderKind.Free2Sms);

        free2Sms.Settings.Where(f => !f.Advanced)
            .Select(f => f.Key)
            .Should().BeEquivalentTo([SmsSettingNames.ApiKey, SmsSettingNames.SenderId]);
    }

    [Fact]
    public void Only_gateways_with_a_native_otp_endpoint_are_flagged_as_supporting_one()
    {
        SmsProviderFieldSchema.For(SmsProviderKind.TwoFactor).SupportsNativeOtp.Should().BeTrue();
        SmsProviderFieldSchema.For(SmsProviderKind.Twilio).SupportsNativeOtp.Should().BeTrue();
        SmsProviderFieldSchema.For(SmsProviderKind.Free2Sms).SupportsNativeOtp.Should().BeFalse();
    }

    [Fact]
    public void Only_a_gateway_with_a_native_otp_endpoint_offers_a_delivery_mode()
    {
        foreach (var provider in SmsProviderKinds.Selectable)
        {
            var offers = SmsProviderFieldSchema.For(provider).Settings
                .Any(f => f.Key == SmsSettingNames.DeliveryMode);

            offers.Should().Be(
                SmsProviderFieldSchema.For(provider).SupportsNativeOtp,
                $"{provider} should only offer a delivery mode when it has a native OTP route");
        }
    }

    [Fact]
    public void Every_field_that_renders_a_select_declares_its_allowed_values()
    {
        foreach (var provider in SmsProviderKinds.Selectable)
        foreach (var field in SmsProviderFieldSchema.For(provider).Settings)
        {
            if (field.Type == SmsFieldType.Select)
                field.AllowedValues.Should().NotBeNullOrEmpty(
                    $"{provider}.{field.Key} is a Select but offers no values");
        }
    }

    [Fact]
    public void Credentials_are_marked_secret_so_the_ui_never_echoes_them_back()
    {
        // Twilio authenticates with an account SID + auth token rather than an API key, so the
        // API-key descriptor only exists for the two gateways that use one.
        foreach (var provider in new[] { SmsProviderKind.TwoFactor, SmsProviderKind.Free2Sms })
        {
            Field(provider, SmsSettingNames.ApiKey).Secret.Should().BeTrue();
            Field(provider, SmsSettingNames.ApiKey).Type.Should().Be(SmsFieldType.Secret);
        }

        Field(SmsProviderKind.Twilio, SmsSettingNames.AuthToken).Secret.Should().BeTrue();
        Field(SmsProviderKind.Twilio, SmsSettingNames.AuthToken).Type.Should().Be(SmsFieldType.Secret);
    }

    [Fact]
    public void Only_actual_credentials_are_marked_secret_not_every_required_field()
    {
        // "Required" and "secret" are different things. A Free2SMS sender ID and a Twilio account
        // SID must both be entered, but both are public identifiers that an admin needs to be able
        // to read back and verify. Masking them would make the form unusable.
        foreach (var provider in SmsProviderKinds.Selectable)
        {
            foreach (var key in new[] { SmsSettingNames.AccountSid, SmsSettingNames.ServiceSid })
            {
                var field = SmsProviderFieldSchema.For(provider).Settings
                    .FirstOrDefault(f => f.Key == key);

                if (field is not null)
                    field.Secret.Should().BeFalse($"{provider}.{key} is a public identifier");
            }
        }

        Field(SmsProviderKind.Free2Sms, SmsSettingNames.SenderId).Secret.Should().BeFalse();
        Field(SmsProviderKind.Free2Sms, SmsSettingNames.SenderId).Required.Should().BeTrue();
    }

    [Fact]
    public void Required_settings_match_the_fields_marked_required()
    {
        // The schema advertises Required and SmsSettingNames.Required drives validation. If those
        // disagree the UI marks something mandatory that the server does not enforce, or worse.
        foreach (var provider in SmsProviderKinds.Selectable)
        {
            var schema = SmsProviderFieldSchema.For(provider);

            SmsSettingNames.Required(provider)
                .Should().BeEquivalentTo(schema.Settings.Where(f => f.Required).Select(f => f.Key));
        }
    }

    [Fact]
    public void No_gateway_requires_a_template_field_unless_the_template_is_shown()
    {
        foreach (var provider in SmsProviderKinds.Selectable)
        {
            var schema = SmsProviderFieldSchema.For(provider);

            schema.TemplateFields.Should().NotBeEmpty();
            schema.RequiresTemplate.Should().BeTrue();
        }
    }

    // -----------------------------------------------------------------------
    // Validation — the server must enforce exactly what it advertises
    // -----------------------------------------------------------------------

    [Fact]
    public void An_omitted_required_credential_is_rejected()
        => SmsProviderFieldSchema.ValidateSetting(SmsProviderKind.TwoFactor, SmsSettingNames.ApiKey, "  ")
            .Should().Be("API key is required.");

    [Fact]
    public void An_omitted_optional_tuning_knob_is_accepted()
        => SmsProviderFieldSchema.ValidateSetting(SmsProviderKind.TwoFactor, SmsSettingNames.OtpPath, null)
            .Should().BeNull();

    [Fact]
    public void An_unlisted_delivery_mode_is_rejected_with_the_permitted_values()
    {
        var error = SmsProviderFieldSchema.ValidateSetting(
            SmsProviderKind.TwoFactor, SmsSettingNames.DeliveryMode, "carrier-pigeon");

        error.Should().Contain("Auto").And.Contain("NativeOtp").And.Contain("TransactionalTemplate");
    }

    [Fact]
    public void A_documented_delivery_mode_is_accepted()
        => SmsProviderFieldSchema.ValidateSetting(
            SmsProviderKind.TwoFactor, SmsSettingNames.DeliveryMode, "nativeotp").Should().BeNull();

    [Fact]
    public void An_endpoint_path_must_start_with_a_slash()
        => SmsProviderFieldSchema.ValidateSetting(
                SmsProviderKind.TwoFactor, SmsSettingNames.OtpPath, "API/V1/OTP/SEND")
            .Should().Contain("must start with '/'");

    [Fact]
    public void A_value_beyond_the_advertised_maximum_length_is_rejected()
    {
        var error = SmsProviderFieldSchema.ValidateSetting(
            SmsProviderKind.TwoFactor, SmsSettingNames.OtpPath, "/" + new string('a', 400));

        error.Should().Contain("must not exceed");
    }

    [Fact]
    public void An_unknown_key_is_not_validated_here_because_it_is_rejected_by_name_elsewhere()
        => SmsProviderFieldSchema.ValidateSetting(SmsProviderKind.TwoFactor, "NotAKey", "x")
            .Should().BeNull();

    // -----------------------------------------------------------------------
    // Template validation
    // -----------------------------------------------------------------------

    [Fact]
    public void A_2factor_template_without_a_name_is_rejected_at_configuration_time()
    {
        // Previously savable, and then failed at send time with TEMPLATE_NOT_CONFIGURED — visible
        // only in logs, days after the mistake.
        var errors = SmsProviderFieldSchema.ValidateTemplate(SmsProviderKind.TwoFactor, "Code {OTP}", null);

        errors.Should().ContainKey("externalTemplateId");
        errors["externalTemplateId"].Should().Contain("required");
    }

    [Fact]
    public void A_2factor_template_with_just_a_name_is_accepted()
    {
        // The native route renders the registered template, so a body is genuinely optional.
        SmsProviderFieldSchema.ValidateTemplate(SmsProviderKind.TwoFactor, null, "LOGIN_OTP")
            .Should().BeEmpty();
    }

    [Fact]
    public void A_malformed_twilio_template_sid_is_rejected_before_it_can_fail_a_send()
    {
        SmsProviderFieldSchema.ValidateTemplate(SmsProviderKind.Twilio, null, "12345")
            .Should().ContainKey("externalTemplateId");
    }

    [Fact]
    public void A_free2sms_template_needs_neither_field_to_be_present()
        => SmsProviderFieldSchema.ValidateTemplate(SmsProviderKind.Free2Sms, "Code {OTP}", "1207161234567890123")
            .Should().BeEmpty();

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
            foreach (var field in schema.Settings.Concat(schema.TemplateFields))
            {
                field.Key.Should().NotBeNullOrWhiteSpace();
                field.Label.Should().NotBeNullOrWhiteSpace();
                field.MaxLength.Should().BeGreaterThan(0);
            }
        }
    }

    [Fact]
    public void A_field_that_is_conditionally_required_explains_the_condition()
    {
        var conditional = SmsProviderFieldSchema.All
            .SelectMany(s => s.TemplateFields.Concat(s.Settings))
            .Where(f => f.RequiredWhen is not null)
            .ToList();

        conditional.Should().NotBeEmpty("the 2Factor template name is conditional and must say so");
        conditional.Should().OnlyContain(f => !string.IsNullOrWhiteSpace(f.RequiredWhen));
    }
}
