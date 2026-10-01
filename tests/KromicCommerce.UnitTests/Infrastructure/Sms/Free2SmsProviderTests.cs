using System.Net;
using KromicCommerce.Application.Abstractions.Sms;
using KromicCommerce.Infrastructure.Sms;
using KromicCommerce.UnitTests.Application;

namespace KromicCommerce.UnitTests.Infrastructure.Sms;

public sealed class Free2SmsProviderTests
{
    private static readonly SmsOtpPolicyOptions Policy = new();

    private static (Free2SmsProvider Provider, StubHttpMessageHandler Handler) Build(
        HttpStatusCode status, string body,
        Free2SmsOptions? options = null,
        ISmsTemplateStore? templates = null,
        ISmsProviderSettings? savedSettings = null)
    {
        var handler = new StubHttpMessageHandler(status, body);
        var provider = new Free2SmsProvider(
            Options.Create(options ?? new Free2SmsOptions { ApiKey = "key-123", SenderId = "F2SMS" }),
            Options.Create(Policy),
            savedSettings ?? SmsTestDoubles.NoSavedSettings(),
            templates ?? SmsTestDoubles.Templates(),
            new StubHttpClientFactory(handler),
            NullLogger<Free2SmsProvider>.Instance);

        return (provider, handler);
    }

    [Fact]
    public async Task Posts_to_the_documented_endpoint_with_a_bearer_token()
    {
        var (provider, handler) = Build(HttpStatusCode.OK,
            """{"success":true,"reference":"SMS-00001234","sms_log_id":1234,"status":"sent"}""");

        var result = await provider.SendOtpAsync("+919876543210", "482917");

        result.Success.Should().BeTrue();
        result.ProviderMessageId.Should().Be("SMS-00001234");

        handler.Request!.Method.Should().Be(HttpMethod.Post);
        handler.Request.RequestUri!.ToString().Should().Be("https://free2sms.com/api/v1/send");
        handler.Request.Headers.GetValues("Authorization").Single().Should().Be("Bearer key-123");
    }

    [Fact]
    public async Task Sends_the_documented_body_with_the_otp_route_and_ten_digit_number()
    {
        var (provider, handler) = Build(HttpStatusCode.OK, """{"success":true}""");

        await provider.SendOtpAsync("+91 98765 43210", "482917");

        var body = handler.RequestBody!;
        body.Should().Contain("\"numbers\":\"9876543210\"");
        body.Should().Contain("\"sender_id\":\"F2SMS\"");
        body.Should().Contain("\"route\":\"otp\"");
        body.Should().Contain("482917");
        body.Should().NotContain("template_id");
    }

    [Fact]
    public async Task Includes_the_DLT_template_id_from_the_managed_template()
    {
        var (provider, handler) = Build(HttpStatusCode.OK, """{"success":true}""",
            templates: SmsTestDoubles.BodyTemplate(
                SmsProviderKind.Free2Sms, "Code {OTP}", "1207161234567890123"));

        await provider.SendOtpAsync("9876543210", "1");

        handler.RequestBody.Should().Contain("\"template_id\":1207161234567890123");
    }

    [Fact]
    public async Task Sends_the_managed_template_body_rather_than_a_hard_coded_one()
    {
        var (provider, handler) = Build(HttpStatusCode.OK, """{"success":true}""",
            templates: SmsTestDoubles.BodyTemplate(
                SmsProviderKind.Free2Sms, "Hi {STORE_NAME}, your code is {OTP}."));

        await provider.SendOtpAsync("9876543210", "482917");

        handler.RequestBody.Should().Contain("your code is 482917");
        handler.RequestBody.Should().NotContain("{OTP}");
    }

    [Fact]
    public async Task Falls_back_to_the_built_in_body_when_no_template_is_managed()
    {
        var (provider, handler) = Build(HttpStatusCode.OK, """{"success":true}""");

        await provider.SendOtpAsync("9876543210", "482917");

        handler.RequestBody.Should().Contain("482917");
        handler.RequestBody.Should().Contain("10 minutes");
    }

    [Fact]
    public async Task An_unparseable_DLT_id_is_omitted_rather_than_sent_malformed()
    {
        var (provider, handler) = Build(HttpStatusCode.OK, """{"success":true}""",
            templates: SmsTestDoubles.BodyTemplate(
                SmsProviderKind.Free2Sms, "Code {OTP}", "not-a-number"));

        var result = await provider.SendOtpAsync("9876543210", "482917");

        result.Success.Should().BeTrue();
        handler.RequestBody.Should().NotContain("template_id");
    }

    [Fact]
    public async Task Reads_a_documented_error_code_from_details()
    {
        var (provider, _) = Build(HttpStatusCode.BadRequest,
            """{"success":false,"error":"Insufficient wallet balance","details":{"code":"TEMPLATE_MISMATCH"}}""");

        var result = await provider.SendOtpAsync("9876543210", "1");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("TEMPLATE_MISMATCH");
        result.Retryable.Should().BeFalse();
    }

    [Fact]
    public async Task Reads_a_rate_limit_code_from_the_top_level_and_treats_it_as_retryable()
    {
        var (provider, _) = Build(HttpStatusCode.TooManyRequests,
            """{"code":"RATE_LIMITED","retryAfter":30}""");

        var result = await provider.SendOtpAsync("9876543210", "1");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("RATE_LIMITED");
        result.Retryable.Should().BeTrue();
    }

    [Fact]
    public async Task Falls_back_to_the_HTTP_status_when_the_body_is_not_json()
    {
        var (provider, _) = Build(HttpStatusCode.BadGateway, "<html>gateway down</html>");

        var result = await provider.SendOtpAsync("9876543210", "1");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("HTTP_502");
        result.Retryable.Should().BeTrue();
    }

    [Fact]
    public async Task A_2xx_that_is_not_success_is_still_a_failure()
    {
        var (provider, _) = Build(HttpStatusCode.OK, """{"success":false}""");

        (await provider.SendOtpAsync("9876543210", "1")).Success.Should().BeFalse();
    }

    [Fact]
    public async Task Refuses_an_unusable_phone_number_without_calling_the_gateway()
    {
        var (provider, handler) = Build(HttpStatusCode.OK, """{"success":true}""");

        var result = await provider.SendOtpAsync("+44 7700 900123", "1");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_PHONE_NUMBER");
        handler.Request.Should().BeNull();
    }

    [Fact]
    public void Reports_its_identity()
    {
        var (provider, _) = Build(HttpStatusCode.OK, """{"success":true}""");

        provider.Kind.Should().Be(SmsProviderKind.Free2Sms);
        provider.ProviderName.Should().Be("Free2SMS");
    }
}
