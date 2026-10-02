using System.Net;
using System.Text.Json;
using KromicCommerce.Application.Abstractions.Sms;
using KromicCommerce.Infrastructure.Configuration;
using KromicCommerce.Infrastructure.Sms;
using KromicCommerce.UnitTests.Application;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KromicCommerce.UnitTests.Infrastructure.Sms;

public sealed class Free2SmsProviderTests
{
    private const string DefaultTemplate = "Your verification code is {{OTP}}. It is valid for 5 minutes.";

    private static readonly Free2SmsOptions Complete = new()
    {
        ApiKey = "key-123",
        SenderId = "F2SMS",
        MessageTemplate = DefaultTemplate
    };

    private static (Free2SmsProvider Provider, StubHttpMessageHandler Handler) Build(
        HttpStatusCode status, string body,
        Free2SmsOptions? options = null,
        SmsProviderSettingsSnapshot? saved = null)
    {
        var handler = new StubHttpMessageHandler(status, body);
        var provider = new Free2SmsProvider(
            Options.Create(options ?? Complete),
            saved,
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
        handler.Request.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.Request.Headers.Authorization.Parameter.Should().Be("key-123");
    }

    [Fact]
    public async Task Substitutes_OTP_into_the_configured_message_template()
    {
        var (provider, handler) = Build(HttpStatusCode.OK, """{"success":true}""");

        await provider.SendOtpAsync("+91 98765 43210", "482917");

        var body = JsonSerializer.Deserialize<Dictionary<string, string>>(handler.RequestBody)!;

        body["message"].Should().Be("Your verification code is 482917. It is valid for 5 minutes.");
        body["sender_id"].Should().Be("F2SMS");
        body["route"].Should().Be("otp");
        body["numbers"].Should().Be("9876543210");
    }

    [Fact]
    public async Task Rejects_a_non_indian_number()
    {
        var (provider, _) = Build(HttpStatusCode.OK, """{"success":true}""");

        var result = await provider.SendOtpAsync("+15550000000", "482917");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_PHONE_NUMBER");
    }

    [Fact]
    public async Task Fails_when_api_key_is_missing()
    {
        var options = new Free2SmsOptions { SenderId = "F2SMS", MessageTemplate = DefaultTemplate };
        var (provider, _) = Build(HttpStatusCode.OK, """{"success":true}""", options: options);

        var result = await provider.SendOtpAsync("+919876543210", "482917");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("MISSING_CREDENTIALS");
    }

    [Fact]
    public async Task Fails_when_sender_id_is_missing()
    {
        var options = new Free2SmsOptions { ApiKey = "k", MessageTemplate = DefaultTemplate };
        var (provider, _) = Build(HttpStatusCode.OK, """{"success":true}""", options: options);

        var result = await provider.SendOtpAsync("+919876543210", "482917");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("MISSING_CREDENTIALS");
    }

    [Fact]
    public async Task Fails_when_message_template_is_missing()
    {
        var options = new Free2SmsOptions { ApiKey = "k", SenderId = "F2SMS" };
        var (provider, _) = Build(HttpStatusCode.OK, """{"success":true}""", options: options);

        var result = await provider.SendOtpAsync("+919876543210", "482917");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("MISSING_TEMPLATE");
    }

    [Fact]
    public async Task Maps_a_template_mismatch_error()
    {
        var (provider, _) = Build(HttpStatusCode.BadRequest,
            """{"success":false,"code":"TEMPLATE_MISMATCH","details":{"code":"TEMPLATE_MISMATCH"}}""");

        var result = await provider.SendOtpAsync("+919876543210", "482917");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("TEMPLATE_MISMATCH");
        result.Retryable.Should().BeFalse();
    }

    [Fact]
    public async Task Maps_an_insufficient_balance_error()
    {
        var (provider, _) = Build(HttpStatusCode.BadRequest,
            """{"success":false,"code":"INSUFFICIENT_BALANCE","details":{"code":"INSUFFICIENT_BALANCE"}}""");

        var result = await provider.SendOtpAsync("+919876543210", "482917");

        result.ErrorCode.Should().Be("INSUFFICIENT_BALANCE");
    }

    [Fact]
    public async Task A_429_is_retryable()
    {
        var (provider, _) = Build(HttpStatusCode.TooManyRequests, """{"code":"RATE_LIMITED"}""");

        var result = await provider.SendOtpAsync("+919876543210", "482917");

        result.Retryable.Should().BeTrue();
    }

    [Fact]
    public async Task A_500_is_retryable()
    {
        var (provider, _) = Build(HttpStatusCode.InternalServerError, """{"success":false}""");

        var result = await provider.SendOtpAsync("+919876543210", "482917");

        result.Retryable.Should().BeTrue();
    }

    [Fact]
    public async Task An_api_network_failure_is_retryable()
    {
        var handler = new StubHttpMessageHandler(
            new HttpRequestException("connection refused"));

        var provider = new Free2SmsProvider(
            Options.Create(Complete),
            null,
            new StubHttpClientFactory(handler),
            NullLogger<Free2SmsProvider>.Instance);

        var result = await provider.SendOtpAsync("+919876543210", "482917");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("HTTP_ERROR");
        result.Retryable.Should().BeTrue();
    }

    [Fact]
    public void Provider_metadata_is_correct()
    {
        var provider = new Free2SmsProvider(
            Options.Create(Complete),
            null,
            new StubHttpClientFactory(new StubHttpMessageHandler(HttpStatusCode.OK, "{}")),
            NullLogger<Free2SmsProvider>.Instance);

        provider.ProviderName.Should().Be("Free2SMS");
        provider.Kind.Should().Be(SmsProviderKind.Free2Sms);
    }
}
