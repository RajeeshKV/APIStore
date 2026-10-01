using System.Net;
using KromicCommerce.Application.Abstractions.Sms;
using KromicCommerce.Infrastructure.Sms;
using KromicCommerce.UnitTests.Application;

namespace KromicCommerce.UnitTests.Infrastructure.Sms;

public sealed class TwilioProviderTests
{
    private static readonly SmsOtpPolicyOptions Policy = new();

    private static readonly TwilioOptions Complete = new()
    {
        AccountSid = "AC00000000000000000000000000000000",
        AuthToken = "auth-token",
        ServiceSid = "VA00000000000000000000000000000000"
    };

    private static (TwilioProvider Provider, StubHttpMessageHandler Handler) Build(
        HttpStatusCode status, string body,
        TwilioOptions? options = null,
        ISmsTemplateStore? templates = null,
        ISmsProviderSettings? savedSettings = null)
    {
        var handler = new StubHttpMessageHandler(status, body);
        var provider = new TwilioProvider(
            Options.Create(options ?? Complete),
            Options.Create(Policy),
            savedSettings ?? SmsTestDoubles.NoSavedSettings(),
            templates ?? SmsTestDoubles.Templates(),
            new StubHttpClientFactory(handler),
            NullLogger<TwilioProvider>.Instance);

        return (provider, handler);
    }

    [Fact]
    public async Task Posts_to_the_documented_verify_endpoint_with_basic_auth()
    {
        var (provider, handler) = Build(HttpStatusCode.Created,
            """{"sid":"VA-test","status":"pending","to":"+919876543210","channel":"sms"}""");

        var result = await provider.SendOtpAsync("+919876543210", "4829");

        result.Success.Should().BeTrue();
        result.ProviderMessageId.Should().Be("VA-test");

        handler.Request!.Method.Should().Be(HttpMethod.Post);
        handler.Request.RequestUri!.ToString().Should()
            .Be($"https://verify.twilio.com/Services/{Complete.ServiceSid}/Verifications");

        var expected = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{Complete.AccountSid}:{Complete.AuthToken}"));
        handler.Request.Headers.Authorization!.Scheme.Should().Be("Basic");
        handler.Request.Headers.Authorization.Parameter.Should().Be(expected);
    }

    [Fact]
    public async Task Sends_our_own_code_so_verification_stays_with_this_application()
    {
        var (provider, handler) = Build(HttpStatusCode.Created, """{"status":"pending"}""");

        await provider.SendOtpAsync("+919876543210", "4829");

        handler.RequestBody.Should().Contain("To=%2B919876543210");
        handler.RequestBody.Should().Contain("Channel=sms");
        handler.RequestBody.Should().Contain("CustomCode=4829");
    }

    [Fact]
    public async Task Sends_a_verify_template_id_when_one_is_managed()
    {
        var (provider, handler) = Build(HttpStatusCode.Created, """{"status":"pending"}""",
            templates: SmsTestDoubles.BodyTemplate(SmsProviderKind.Twilio, "", "HJ1234567890abcdef"));

        await provider.SendOtpAsync("+919876543210", "4829");

        handler.RequestBody.Should().Contain("TemplateSid=HJ1234567890abcdef");
        // No substitutions configured, so CustomCode is kept.
        handler.RequestBody.Should().Contain("CustomCode=4829");
        handler.RequestBody.Should().NotContain("TemplateCustomSubstitutions");
    }

    [Fact]
    public async Task A_template_with_a_body_becomes_custom_substitutions_instead_of_a_custom_code()
    {
        var (provider, handler) = Build(HttpStatusCode.Created, """{"status":"pending"}""",
            templates: SmsTestDoubles.BodyTemplate(
                SmsProviderKind.Twilio, "Your code is {OTP}, valid {EXPIRY_MINUTES} minutes.", "HJ123"));

        await provider.SendOtpAsync("+919876543210", "4829");

        handler.RequestBody.Should().Contain("TemplateSid=HJ123");
        // The registered body defines the substitution keys, so CustomCode is not valid there.
        handler.RequestBody.Should().NotContain("CustomCode=");
        handler.RequestBody.Should().Contain("TemplateCustomSubstitutions=");
        handler.RequestBody.Should().Contain("4829");
    }

    [Fact]
    public async Task Includes_the_messaging_service_sid_when_configured()
    {
        var (provider, handler) = Build(HttpStatusCode.Created, """{"status":"pending"}""",
            options: new TwilioOptions
            {
                AccountSid = Complete.AccountSid,
                AuthToken = Complete.AuthToken,
                ServiceSid = Complete.ServiceSid,
                MessagingServiceSid = "MG123"
            });

        await provider.SendOtpAsync("+919876543210", "4829");

        handler.RequestBody.Should().Contain("MessagingServiceSid=MG123");
    }

    [Fact]
    public async Task Reads_a_documented_twilio_error_code()
    {
        var (provider, _) = Build(HttpStatusCode.BadRequest,
            """{"code":20404,"message":"The destination number is unverified","status":403}""");

        var result = await provider.SendOtpAsync("+919876543210", "4829");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("20404");
        // A different destination number would succeed, so this is worth retrying.
        result.Retryable.Should().BeTrue();
    }

    [Fact]
    public async Task A_bad_auth_token_is_not_retryable()
    {
        var (provider, _) = Build(HttpStatusCode.Unauthorized,
            """{"code":20003,"message":"Authenticate","status":401}""");

        var result = await provider.SendOtpAsync("+919876543210", "4829");

        result.ErrorCode.Should().Be("20003");
        result.Retryable.Should().BeFalse();
    }

    [Fact]
    public async Task Refuses_a_national_number_because_it_cannot_infer_a_country_code()
    {
        var (provider, handler) = Build(HttpStatusCode.Created, """{"status":"pending"}""");

        var result = await provider.SendOtpAsync("9876543210", "4829");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_PHONE_NUMBER");
        handler.Request.Should().BeNull();
    }

    [Fact]
    public async Task Normalises_spaced_and_punctuated_international_numbers()
    {
        var (provider, handler) = Build(HttpStatusCode.Created, """{"status":"pending"}""");

        var result = await provider.SendOtpAsync("+91 98765-43210", "4829");

        result.Success.Should().BeTrue();
        handler.RequestBody.Should().Contain("To=%2B919876543210");
    }

    [Fact]
    public async Task Administrator_saved_settings_override_configuration()
    {
        var (provider, handler) = Build(HttpStatusCode.Created, """{"status":"pending"}""",
            options: new TwilioOptions
            {
                AccountSid = Complete.AccountSid,
                AuthToken = "from-config",
                ServiceSid = Complete.ServiceSid
            },
            savedSettings: SmsTestDoubles.SavedSettings(
                SmsProviderKind.Twilio, true, ("AuthToken", "from-admin"), ("ServiceSid", "VA-admin")));

        await provider.SendOtpAsync("+919876543210", "4829");

        var expected = Convert.ToBase64String(System.Text.Encoding.UTF8
            .GetBytes($"{Complete.AccountSid}:from-admin"));
        handler.Request.Headers.Authorization!.Parameter.Should().Be(expected);
        handler.Request.RequestUri!.ToString().Should().Contain("VA-admin");
    }

    [Fact]
    public async Task A_saved_selection_for_another_provider_blocks_the_send()
    {
        var (provider, handler) = Build(HttpStatusCode.Created, """{"status":"pending"}""",
            savedSettings: SmsTestDoubles.SavedSettings(
                SmsProviderKind.Free2Sms, true, ("ApiKey", "k"), ("SenderId", "F2SMS")));

        var result = await provider.SendOtpAsync("+919876543210", "4829");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("PROVIDER_NOT_SELECTED");
        handler.Request.Should().BeNull();
    }

    [Fact]
    public async Task A_saved_disabled_selection_blocks_the_send()
    {
        var (provider, handler) = Build(HttpStatusCode.Created, """{"status":"pending"}""",
            savedSettings: SmsTestDoubles.SavedSettings(
                SmsProviderKind.Twilio, false, ("AccountSid", "AC"), ("AuthToken", "t"), ("ServiceSid", "VA")));

        var result = await provider.SendOtpAsync("+919876543210", "4829");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("SMS_NOT_CONFIGURED");
        handler.Request.Should().BeNull();
    }

    [Fact]
    public void Reports_its_identity()
    {
        var (provider, _) = Build(HttpStatusCode.Created, """{"status":"pending"}""");

        provider.Kind.Should().Be(SmsProviderKind.Twilio);
        provider.ProviderName.Should().Be("Twilio");
    }
}
