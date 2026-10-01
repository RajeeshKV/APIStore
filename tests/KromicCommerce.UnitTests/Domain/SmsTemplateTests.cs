using KromicCommerce.Infrastructure.Auth;

namespace KromicCommerce.UnitTests.Domain;

public sealed class SmsTemplateTests
{
    [Fact]
    public void A_template_needs_a_body_or_a_vendor_identifier()
    {
        var neither = () => SmsTemplate.Create(SmsProviderKind.Free2Sms, "Code", null, null);
        var either = () => SmsTemplate.Create(SmsProviderKind.Free2Sms, "Code", "Your code is {OTP}", null);

        neither.Should().Throw<ArgumentException>();
        either.Should().NotThrow();
    }

    [Fact]
    public void A_vendor_identifier_alone_is_enough_for_a_gateway_that_renders_its_own_copy()
    {
        var template = SmsTemplate.Create(SmsProviderKind.Twilio, "Verify template", null, "HJ123");

        template.Body.Should().BeEmpty();
        template.ExternalTemplateId.Should().Be("HJ123");
    }

    [Fact]
    public void A_template_cannot_be_created_for_the_none_provider()
        => ((Action)(() => SmsTemplate.Create(SmsProviderKind.None, "Code", "Code {OTP}", null)))
            .Should().Throw<ArgumentException>();

    [Fact]
    public void Values_are_trimmed_and_blank_identifiers_are_stored_as_null()
    {
        var template = SmsTemplate.Create(
            SmsProviderKind.Free2Sms, "  Verification  ", "  Code {OTP}  ", "   ");

        template.Name.Should().Be("Verification");
        template.Body.Should().Be("Code {OTP}");
        template.ExternalTemplateId.Should().BeNull();
    }

    [Fact]
    public void Rendering_substitutes_every_placeholder()
    {
        var template = SmsTemplate.Create(
            SmsProviderKind.Free2Sms,
            "Verification",
            "Hi {STORE_NAME}, your code is {OTP} and expires in {EXPIRY_MINUTES} minutes.",
            null);

        template.Render("4829", 10, "Acme")
            .Should().Be("Hi Acme, your code is 4829 and expires in 10 minutes.");
    }

    [Fact]
    public void An_unset_store_name_renders_empty_rather_than_leaving_the_placeholder()
    {
        var template = SmsTemplate.Create(SmsProviderKind.Free2Sms, "V", "Hi {STORE_NAME}!", null);

        template.Render("4829", 10).Should().Be("Hi !");
    }

    [Fact]
    public void Update_rejects_removing_both_the_body_and_the_vendor_identifier()
    {
        var template = SmsTemplate.Create(SmsProviderKind.Twilio, "V", null, "HJ123");

        ((Action)(() => template.Update("V", null, null))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Activation_toggles_without_touching_the_content()
    {
        var template = SmsTemplate.Create(SmsProviderKind.Free2Sms, "V", "Code {OTP}", null);

        template.Deactivate();
        template.IsActive.Should().BeFalse();

        template.Activate();
        template.IsActive.Should().BeTrue();
        template.Body.Should().Be("Code {OTP}");
    }
}

public sealed class OtpServiceTests
{
    private static IOtpService Service => new OtpService();

    [Fact]
    public void Codes_are_four_digits_by_default()
        => Service.GenerateOtp().Should().MatchRegex("^\\d{4}$");

    [Fact]
    public void Generated_codes_are_only_digits()
        => Service.GenerateOtp().Should().MatchRegex("^\\d+$");

    [Fact]
    public void An_explicit_length_is_honoured()
        => Service.GenerateOtp(6).Should().MatchRegex("^\\d{6}$");

    [Theory]
    [InlineData(3)]
    [InlineData(11)]
    public void An_out_of_range_length_is_refused(int length)
        => ((Action)(() => Service.GenerateOtp(length)))
            .Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void Verification_round_trips_and_rejects_a_wrong_code()
    {
        var hash = Service.HashOtp("4829");

        Service.VerifyOtp("4829", hash).Should().BeTrue();
        Service.VerifyOtp("4830", hash).Should().BeFalse();
    }
}
