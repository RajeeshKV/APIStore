using System.Net;
using KromicCommerce.Application.Abstractions.Sms;
using KromicCommerce.Infrastructure.Sms;
using KromicCommerce.UnitTests.Application;

namespace KromicCommerce.UnitTests.Infrastructure.Sms;

public sealed class TwoFactorProviderTests
{
    private static readonly SmsOtpPolicyOptions Policy = new();

    private static readonly TwoFactorOptions Complete = new() { ApiKey = "2fa-secret" };

    private static ISmsTemplateStore ApprovedTemplate() => SmsTestDoubles.BodyTemplate(
        SmsProviderKind.TwoFactor, "", "APPROVED-TEMPLATE-1");

    private static (TwoFactorProvider Provider, StubHttpMessageHandler Handler) Build(
        HttpStatusCode status, string body,
        TwoFactorOptions? options = null,
        ISmsTemplateStore? templates = null,
        SmsProviderSettingsSnapshot? saved = null)
    {
        var handler = new StubHttpMessageHandler(status, body);
        var provider = new TwoFactorProvider(
            Options.Create(options ?? Complete),
            Options.Create(Policy),
            saved,
            templates ?? ApprovedTemplate(),
            new StubHttpClientFactory(handler),
            NullLogger<TwoFactorProvider>.Instance);

        return (provider, handler);
    }

    [Fact]
    public async Task Posts_the_code_and_template_id_to_the_configured_send_path()
    {
        var (provider, handler) = Build(HttpStatusCode.OK,
            """{"Status":"Success","Details":"SMS sent to the recipient","SessionId":"SESS-1"}""");

        var result = await provider.SendOtpAsync("+919876543210", "4829");

        result.Success.Should().BeTrue();
        result.ProviderMessageId.Should().Be("SESS-1");

        handler.Request!.RequestUri!.ToString().Should().Be("https://api.2factor.in/v1/sms/send");
        handler.RequestBody.Should().Contain("\"apiKey\":\"2fa-secret\"");
        handler.RequestBody.Should().Contain("\"to\":\"+919876543210\"");
        handler.RequestBody.Should().Contain("\"templateId\":\"APPROVED-TEMPLATE-1\"");
        handler.RequestBody.Should().Contain("\"otp\":\"4829\"");
    }

    [Fact]
    public async Task The_expiry_variable_carries_the_policy_value_under_its_configured_name()
    {
        var (provider, handler) = Build(HttpStatusCode.OK, """{"Status":"Success"}""",
            options: new TwoFactorOptions
            {
                ApiKey = Complete.ApiKey,
                ExpiryVariableName = "validity"
            });

        await provider.SendOtpAsync("+919876543210", "4829");

        handler.RequestBody.Should().Contain("\"validity\":\"10\"");
    }

    [Fact]
    public async Task Refuses_to_send_without_an_approved_template()
    {
        var (provider, handler) = Build(HttpStatusCode.OK, """{"Status":"Success"}""",
            templates: SmsTestDoubles.Templates());

        var result = await provider.SendOtpAsync("+919876543210", "4829");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("TEMPLATE_NOT_CONFIGURED");
        handler.Request.Should().BeNull();
    }

    [Fact]
    public async Task A_body_only_template_is_not_enough_because_the_template_id_is_required()
    {
        var (provider, _) = Build(HttpStatusCode.OK, """{"Status":"Success"}""",
            templates: SmsTestDoubles.BodyTemplate(SmsProviderKind.TwoFactor, "Code {OTP}"));

        (await provider.SendOtpAsync("+919876543210", "4829"))
            .ErrorCode.Should().Be("TEMPLATE_NOT_CONFIGURED");
    }

    [Fact]
    public async Task A_2xx_carrying_an_explicit_error_status_is_still_a_failure()
    {
        // 2Factor has historically reported application failures as HTTP 200 with a Status field.
        var (provider, _) = Build(HttpStatusCode.OK,
            """{"Status":"Error","Details":"The template is not approved for this account"}""");

        var result = await provider.SendOtpAsync("+919876543210", "4829");

        result.Success.Should().BeFalse();
        result.Retryable.Should().BeFalse();
    }

    [Fact]
    public async Task A_gateway_failure_is_retryable()
    {
        var (provider, _) = Build(HttpStatusCode.ServiceUnavailable, "gateway down");

        var result = await provider.SendOtpAsync("+919876543210", "4829");

        result.Success.Should().BeFalse();
        result.Retryable.Should().BeTrue();
    }

    [Fact]
    public async Task Refuses_a_number_without_a_country_code()
    {
        var (provider, handler) = Build(HttpStatusCode.OK, """{"Status":"Success"}""");

        var result = await provider.SendOtpAsync("9876543210", "4829");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_PHONE_NUMBER");
        handler.Request.Should().BeNull();
    }

    [Fact]
    public async Task Missing_credentials_are_reported_without_calling_the_gateway()
    {
        var (provider, handler) = Build(HttpStatusCode.OK, """{"Status":"Success"}""",
            options: new TwoFactorOptions { ApiKey = "" });

        var result = await provider.SendOtpAsync("+919876543210", "4829");

        result.ErrorCode.Should().Be("MISSING_CREDENTIALS");
        handler.Request.Should().BeNull();
    }

    [Fact]
    public async Task The_send_path_can_be_corrected_in_configuration_without_a_rebuild()
    {
        var (provider, handler) = Build(HttpStatusCode.OK, """{"Status":"Success"}""",
            options: new TwoFactorOptions
            {
                ApiKey = Complete.ApiKey,
                BaseUrl = "https://proxy.internal/2fa",
                SendPath = "/v9/sms"
            });

        await provider.SendOtpAsync("+919876543210", "4829");

        handler.Request!.RequestUri!.ToString().Should().Be("https://proxy.internal/2fa/v9/sms");
    }


    [Fact]
    public void Reports_its_identity()
    {
        var (provider, _) = Build(HttpStatusCode.OK, """{"Status":"Success"}""");

        provider.Kind.Should().Be(SmsProviderKind.TwoFactor);
        provider.ProviderName.Should().Be("2Factor");
    }
}
