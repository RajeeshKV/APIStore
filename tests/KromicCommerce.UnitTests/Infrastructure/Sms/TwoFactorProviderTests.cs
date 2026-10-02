using System.Net;
using System.Text.Json;
using KromicCommerce.Application.Abstractions.Sms;
using KromicCommerce.Infrastructure.Configuration;
using KromicCommerce.Infrastructure.Sms;
using KromicCommerce.UnitTests.Application;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KromicCommerce.UnitTests.Infrastructure.Sms;

public sealed class TwoFactorProviderTests
{
    private static readonly TwoFactorOptions Complete = new() { ApiKey = "2fa-secret", TemplateName = "LOGIN_OTP" };

    private const string Accepted = """{"Status":"Success","SessionId":"SESS-1"}""";

    private static (TwoFactorProvider Provider, RecordingHttpMessageHandler Handler) Build(
        HttpStatusCode status = HttpStatusCode.OK,
        string body = Accepted,
        TwoFactorOptions? options = null,
        SmsProviderSettingsSnapshot? saved = null)
    {
        var handler = new RecordingHttpMessageHandler(status, body);
        var provider = new TwoFactorProvider(
            Options.Create(options ?? Complete),
            saved,
            new StubHttpClientFactory(handler),
            NullLogger<TwoFactorProvider>.Instance);

        return (provider, handler);
    }

    [Fact]
    public async Task Posts_to_the_documented_OTP_endpoint_with_api_key_header()
    {
        var (provider, handler) = Build();

        var result = await provider.SendOtpAsync("+919876543210", "4829");

        result.Success.Should().BeTrue();
        result.ProviderMessageId.Should().Be("SESS-1");

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].PathAndQuery.Should().Be("/API/V1/OTP/SEND");
        handler.Requests[0].Headers["X-API-Key"].Should().Be("2fa-secret");
    }

    [Fact]
    public async Task Sends_the_generated_otp_as_the_first_template_variable()
    {
        var (provider, handler) = Build();

        await provider.SendOtpAsync("+919876543210", "4829");

        var body = JsonSerializer.Deserialize<Dictionary<string, string>>(handler.Requests[0].Body);

        body!["to"].Should().Be("+919876543210");
        body!["channel"].Should().Be("SMS");
        body!["template_name"].Should().Be("LOGIN_OTP");
        body!["var1"].Should().Be("4829");
    }

    [Fact]
    public async Task Rejects_a_non_indian_number()
    {
        var (provider, _) = Build();

        var result = await provider.SendOtpAsync("+15550000000", "4829");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_PHONE_NUMBER");
    }

    [Fact]
    public async Task Fails_when_api_key_is_missing()
    {
        var (provider, _) = Build(options: new TwoFactorOptions { TemplateName = "LOGIN_OTP" });

        var result = await provider.SendOtpAsync("+919876543210", "4829");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("MISSING_CREDENTIALS");
    }

    [Fact]
    public async Task Fails_when_template_name_is_missing()
    {
        var (provider, _) = Build(options: new TwoFactorOptions { ApiKey = "key" });

        var result = await provider.SendOtpAsync("+919876543210", "4829");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("MISSING_TEMPLATE");
    }

    [Fact]
    public async Task Maps_a_200_with_status_sent_to_success()
    {
        var (provider, _) = Build(HttpStatusCode.OK, """{"status":"sent","session_id":"ABC"}""");

        var result = await provider.SendOtpAsync("+919876543210", "4829");

        result.Success.Should().BeTrue();
        result.ProviderMessageId.Should().Be("ABC");
    }

    [Fact]
    public async Task Maps_a_failed_status_to_a_failure()
    {
        var (provider, _) = Build(HttpStatusCode.OK, """{"status":"Error","code":"INVALID_API_KEY"}""");

        var result = await provider.SendOtpAsync("+919876543210", "4829");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_API_KEY");
        result.Retryable.Should().BeFalse();
    }

    [Fact]
    public async Task A_429_is_retryable()
    {
        var (provider, _) = Build(HttpStatusCode.TooManyRequests, """{"code":"RATE_LIMITED"}""");

        var result = await provider.SendOtpAsync("+919876543210", "4829");

        result.Retryable.Should().BeTrue();
    }

    [Fact]
    public async Task A_503_is_retryable()
    {
        var (provider, _) = Build(HttpStatusCode.ServiceUnavailable, """{"code":"GATEWAY_UNAVAILABLE"}""");

        var result = await provider.SendOtpAsync("+919876543210", "4829");

        result.Retryable.Should().BeTrue();
    }

    [Fact]
    public async Task An_invalid_api_key_error_is_not_retryable()
    {
        var (provider, _) = Build(HttpStatusCode.Unauthorized, """{"status":"Error","code":"UNAUTHORIZED"}""");

        var result = await provider.SendOtpAsync("+919876543210", "4829");

        result.Retryable.Should().BeFalse();
        result.ErrorCode.Should().Be("UNAUTHORIZED");
    }

    [Fact]
    public async Task An_api_network_failure_is_retryable()
    {
        var handler = new RecordingHttpMessageHandler(
            new HttpRequestException("connection refused"));

        var provider = new TwoFactorProvider(
            Options.Create(Complete),
            null,
            new StubHttpClientFactory(handler),
            NullLogger<TwoFactorProvider>.Instance);

        var result = await provider.SendOtpAsync("+919876543210", "4829");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("HTTP_ERROR");
        result.Retryable.Should().BeTrue();
    }

    [Fact]
    public void Provider_metadata_is_correct()
    {
        var provider = new TwoFactorProvider(
            Options.Create(Complete),
            null,
            new StubHttpClientFactory(new StubHttpMessageHandler(HttpStatusCode.OK, "{}")),
            NullLogger<TwoFactorProvider>.Instance);

        provider.ProviderName.Should().Be("2Factor");
        provider.Kind.Should().Be(SmsProviderKind.TwoFactor);
    }
}
