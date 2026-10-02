using System.Net;
using System.Text.Json;
using KromicCommerce.Application.Abstractions.Sms;
using KromicCommerce.Infrastructure.Configuration;
using KromicCommerce.Infrastructure.Sms;
using KromicCommerce.UnitTests.Application;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KromicCommerce.UnitTests.Infrastructure.Sms;

public sealed class TwilioProviderTests
{
    private static readonly TwilioOptions Complete = new()
    {
        AccountSid = "AC00000000000000000000000000000000",
        AuthToken = "auth-token",
        FromNumber = "+15550000000"
    };

    private static (TwilioProvider Provider, StubHttpMessageHandler Handler) Build(
        HttpStatusCode status, string body,
        TwilioOptions? options = null,
        SmsProviderSettingsSnapshot? saved = null)
    {
        var handler = new StubHttpMessageHandler(status, body);
        var provider = new TwilioProvider(
            Options.Create(options ?? Complete),
            saved,
            new StubHttpClientFactory(handler),
            NullLogger<TwilioProvider>.Instance);

        return (provider, handler);
    }

    [Fact]
    public async Task Posts_to_the_Powerful_Messaging_endpoint_with_basic_auth()
    {
        var (provider, handler) = Build(HttpStatusCode.Created,
            """{"sid":"SM-test","status":"queued","to":"+919876543210","from":"+15550000000"}""");

        var result = await provider.SendOtpAsync("+919876543210", "4829");

        result.Success.Should().BeTrue();
        result.ProviderMessageId.Should().Be("SM-test");

        handler.Request!.Method.Should().Be(HttpMethod.Post);
        handler.Request.RequestUri!.ToString().Should()
            .Be($"https://api.twilio.com/2010-04-01/Accounts/{Complete.AccountSid}/Messages.json");

        handler.Request.Headers.Authorization!.Scheme.Should().Be("Basic");
        var expected = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{Complete.AccountSid}:{Complete.AuthToken}"));
        handler.Request.Headers.Authorization.Parameter.Should().Be(expected);
    }

    [Fact]
    public async Task Sends_the_generated_otp_in_the_message_body()
    {
        var (provider, handler) = Build(HttpStatusCode.Created, """{"sid":"SM-test"}""");

        await provider.SendOtpAsync("+919876543210", "482917");

        handler.RequestBody.Should().Contain("To=%2B919876543210");
        handler.RequestBody.Should().Contain("From=%2B15550000000");
        handler.RequestBody.Should().Contain("Body=");
        handler.RequestBody.Should().Contain("482917");
    }

    [Fact]
    public async Task Rejects_a_non_e164_number()
    {
        var (provider, _) = Build(HttpStatusCode.Created, """{"sid":"SM-test"}""");

        var result = await provider.SendOtpAsync("not-a-phone-number", "4829");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_PHONE_NUMBER");
    }

    [Fact]
    public async Task Fails_when_credentials_are_missing()
    {
        var options = new TwilioOptions { AccountSid = "AC123" };
        var (provider, _) = Build(HttpStatusCode.Created, """{"sid":"SM"}""", options: options);

        var result = await provider.SendOtpAsync("+919876543210", "4829");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("MISSING_CREDENTIALS");
    }

    [Fact]
    public async Task Maps_a_20404_to_invalid_number()
    {
        var (provider, _) = Build(HttpStatusCode.BadRequest,
            """{"code":20404,"message":"Not a valid destination"}""");

        var result = await provider.SendOtpAsync("+919876543210", "4829");

        result.ErrorCode.Should().Be("20404");
        result.Retryable.Should().BeFalse();
    }

    [Fact]
    public async Task Maps_an_invalid_sender_error()
    {
        var (provider, _) = Build(HttpStatusCode.BadRequest,
            """{"code":21212,"message":"Invalid From number"}""");

        var result = await provider.SendOtpAsync("+919876543210", "4829");

        result.ErrorCode.Should().Be("21212");
    }

    [Fact]
    public async Task A_500_is_retryable()
    {
        var (provider, _) = Build(HttpStatusCode.InternalServerError,
            """{"message":"Internal error"}""");

        var result = await provider.SendOtpAsync("+919876543210", "4829");

        result.Retryable.Should().BeTrue();
    }

    [Fact]
    public async Task An_invalid_auth_token_is_not_retryable()
    {
        var (provider, _) = Build(HttpStatusCode.Unauthorized,
            """{"code":20003,"message":"Authentication Error"}""");

        var result = await provider.SendOtpAsync("+919876543210", "4829");

        result.Retryable.Should().BeFalse();
        result.ErrorCode.Should().Be("20003");
    }

    [Fact]
    public async Task An_api_network_failure_is_retryable()
    {
        var handler = new StubHttpMessageHandler(
            new HttpRequestException("connection refused"));

        var provider = new TwilioProvider(
            Options.Create(Complete),
            null,
            new StubHttpClientFactory(handler),
            NullLogger<TwilioProvider>.Instance);

        var result = await provider.SendOtpAsync("+919876543210", "4829");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("HTTP_ERROR");
        result.Retryable.Should().BeTrue();
    }

    [Fact]
    public void Provider_metadata_is_correct()
    {
        var provider = new TwilioProvider(
            Options.Create(Complete),
            null,
            new StubHttpClientFactory(new StubHttpMessageHandler(HttpStatusCode.Created, "{}")),
            NullLogger<TwilioProvider>.Instance);

        provider.ProviderName.Should().Be("Twilio");
        provider.Kind.Should().Be(SmsProviderKind.Twilio);
    }
}
