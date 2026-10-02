using KromicCommerce.Application.Abstractions.Sms;
using KromicCommerce.Domain.Sms;
using KromicCommerce.Infrastructure.Sms;
using KromicCommerce.UnitTests.Application;

namespace KromicCommerce.UnitTests.Infrastructure.Sms;

/// <summary>
/// The delivery-mode vocabulary and the template resolution every adapter shares.
/// </summary>
public sealed class SmsOtpDeliveryModeTests
{
    [Theory]
    [InlineData("Auto", SmsOtpDeliveryMode.Auto)]
    [InlineData("auto", SmsOtpDeliveryMode.Auto)]
    [InlineData("  default ", SmsOtpDeliveryMode.Auto)]
    [InlineData("NativeOtp", SmsOtpDeliveryMode.NativeOtp)]
    [InlineData("native-otp", SmsOtpDeliveryMode.NativeOtp)]
    [InlineData("hosted", SmsOtpDeliveryMode.NativeOtp)]
    [InlineData("TransactionalTemplate", SmsOtpDeliveryMode.TransactionalTemplate)]
    [InlineData("transactional", SmsOtpDeliveryMode.TransactionalTemplate)]
    [InlineData("CUSTOM_TEMPLATE", SmsOtpDeliveryMode.TransactionalTemplate)]
    public void Parses_every_documented_spelling(string input, SmsOtpDeliveryMode expected)
        => SmsOtpDeliveryModes.Parse(input).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("carrier-pigeon")]
    [InlineData("2")]
    public void Refuses_anything_else_rather_than_guessing(string? input)
        => SmsOtpDeliveryModes.Parse(input).Should().BeNull();

    [Fact]
    public void Round_trips_through_its_canonical_name()
    {
        foreach (var mode in SmsOtpDeliveryModes.All)
            SmsOtpDeliveryModes.Parse(mode.ToName()).Should().Be(mode);
    }

    [Fact]
    public void Auto_is_the_default_so_turning_the_setting_on_is_always_safe()
        => SmsOtpDeliveryModes.All[0].Should().Be(SmsOtpDeliveryMode.Auto);
}

public sealed class SmsTemplateResolverTests
{
    [Fact]
    public async Task Resolves_nothing_when_no_template_is_configured()
    {
        var result = await SmsTemplateResolver.ResolveAsync(
            SmsTestDoubles.Templates(), SmsProviderKind.TwoFactor, "4829", 10, default);

        result.Exists.Should().BeFalse();
        result.HasReference.Should().BeFalse();
        result.HasBody.Should().BeFalse();
        result.Variables.Should().BeEmpty();
    }

    [Fact]
    public async Task Takes_the_vendor_reference_from_the_template_row_not_from_configuration()
    {
        // For 2Factor this field holds the template NAME, which is the whole point of the setting.
        var store = SmsTestDoubles.BodyTemplate(SmsProviderKind.TwoFactor, "", "LOGIN_OTP");

        var result = await SmsTemplateResolver.ResolveAsync(
            store, SmsProviderKind.TwoFactor, "4829", 10, default);

        result.Reference.Should().Be("LOGIN_OTP");
        result.HasReference.Should().BeTrue();
    }

    [Fact]
    public async Task Extracts_the_placeholders_in_the_order_the_body_writes_them()
    {
        // Positional gateways (2Factor var1/var2) depend on this ordering being stable and derived
        // from the body the administrator actually wrote.
        var store = SmsTestDoubles.BodyTemplate(
            SmsProviderKind.TwoFactor, "{EXPIRY_MINUTES} minutes: {OTP}");

        var result = await SmsTemplateResolver.ResolveAsync(
            store, SmsProviderKind.TwoFactor, "4829", 7, default);

        result.Variables.Select(v => v.Key).Should().ContainInOrder("{EXPIRY_MINUTES}", "{OTP}");
        result.Variables[0].Value.Should().Be("7");
        result.Variables[1].Value.Should().Be("4829");
    }

    [Fact]
    public async Task Does_not_repeat_a_placeholder_that_appears_twice()
    {
        var store = SmsTestDoubles.BodyTemplate(
            SmsProviderKind.TwoFactor, "{OTP} then {OTP} again");

        var result = await SmsTemplateResolver.ResolveAsync(
            store, SmsProviderKind.TwoFactor, "4829", 10, default);

        result.Variables.Should().ContainSingle();
        result.Render().Should().Be("4829 then 4829 again");
    }

    [Fact]
    public async Task Passes_an_unrecognised_token_through_as_empty_rather_than_leaving_it_literal()
    {
        // A token the gateway cannot fill would otherwise reach the customer as literal "{X}".
        var store = SmsTestDoubles.BodyTemplate(
            SmsProviderKind.TwoFactor, "Code {OTP} for {STORE_NAME}");

        var result = await SmsTemplateResolver.ResolveAsync(
            store, SmsProviderKind.TwoFactor, "4829", 10, default);

        result.Variables.Should().Contain(v => v.Key == "{STORE_NAME}" && v.Value == string.Empty);
        result.Render().Should().Be("Code 4829 for ");
    }

    [Fact]
    public async Task Renders_the_body_with_both_known_tokens()
    {
        var store = SmsTestDoubles.BodyTemplate(
            SmsProviderKind.TwoFactor, "Your code is {OTP}, valid {EXPIRY_MINUTES} min.");

        var result = await SmsTemplateResolver.ResolveAsync(
            store, SmsProviderKind.TwoFactor, "4829", 10, default);

        result.Render().Should().Be("Your code is 4829, valid 10 min.");
    }

    [Fact]
    public async Task Renders_empty_for_a_reference_only_template()
    {
        // The signal to let the gateway render its own registered copy.
        var store = SmsTestDoubles.BodyTemplate(SmsProviderKind.Twilio, "", "HJ0123456789ABCDEF");

        var result = await SmsTemplateResolver.ResolveAsync(
            store, SmsProviderKind.Twilio, "4829", 10, default);

        result.HasBody.Should().BeFalse();
        result.Render().Should().BeEmpty();
    }
}

public sealed class SmsSettingNamesTests
{
    [Theory]
    [InlineData(SmsProviderKind.TwoFactor, "OtpPath")]
    [InlineData(SmsProviderKind.TwoFactor, "TransactionalPath")]
    [InlineData(SmsProviderKind.TwoFactor, "ApiKeyHeader")]
    [InlineData(SmsProviderKind.TwoFactor, "TemplateNameField")]
    [InlineData(SmsProviderKind.TwoFactor, "DeliveryMode")]
    // SendPath stays accepted so a row saved before the native/transactional split still validates.
    [InlineData(SmsProviderKind.TwoFactor, "SendPath")]
    [InlineData(SmsProviderKind.Twilio, "SenderId")]
    [InlineData(SmsProviderKind.Twilio, "MessagingPath")]
    [InlineData(SmsProviderKind.Free2Sms, "DeliveryMode")]
    public void Accepts_the_new_route_and_template_settings(SmsProviderKind provider, string name)
        => SmsSettingNames.IsKnown(provider, name).Should().BeTrue();

    [Theory]
    [InlineData(SmsProviderKind.TwoFactor, "TemplateName")]      // plausible typo, must be rejected
    [InlineData(SmsProviderKind.TwoFactor, "transactional_path")] // not a real setting
    [InlineData(SmsProviderKind.Twilio, "OtpPath")]               // real, but only for 2Factor
    [InlineData(SmsProviderKind.Free2Sms, "OtpPath")]
    [InlineData(SmsProviderKind.Free2Sms, "MessagingPath")]
    public void Still_rejects_misspelled_and_cross_provider_names(SmsProviderKind provider, string name)
        => SmsSettingNames.IsKnown(provider, name).Should().BeFalse();

    [Fact]
    public void Required_settings_are_a_subset_of_all_settings()
    {
        foreach (var provider in SmsProviderKinds.Selectable)
            SmsSettingNames.All(provider).Should().Contain(SmsSettingNames.Required(provider));
    }

    [Fact]
    public void All_has_no_duplicates()
    {
        foreach (var provider in SmsProviderKinds.Selectable)
            SmsSettingNames.All(provider).Should().OnlyHaveUniqueItems();
    }
}
