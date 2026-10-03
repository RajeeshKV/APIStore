using System.Net;
using KromicCommerce.Application.Abstractions.Sms;
using KromicCommerce.Infrastructure.Configuration;
using KromicCommerce.Infrastructure.Sms;
using KromicCommerce.UnitTests.Application;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KromicCommerce.UnitTests.Infrastructure.Sms;

/// <summary>
/// Pins the request shape against 2Factor's own API documentation, not against whatever the
/// adapter happens to build.
/// </summary>
/// <remarks>
/// The previous version of this file asserted the URL, the <c>X-API-Key</c> header and the JSON
/// body that the adapter also asserted, so the two agreed with each other while both disagreed
/// with 2Factor — and production answered <c>404</c> on every send. The assertions below name the
/// documented contract instead, which is the only thing that can catch that class of mistake.
/// </remarks>
public sealed class TwoFactorProviderTests
{
    private static readonly TwoFactorOptions Complete = new() { ApiKey = "2fa-secret", TemplateName = "LOGIN_OTP" };

    private const string Accepted = """{"Status":"Success","Details":"OTP sent successfully to 919876543210"}""";

    /// <summary>
    /// Documented route: <c>/API/V1/{api_key}/SMS/{phone}/{otp}/{template_name}</c>. The API key is
    /// a path segment — 2Factor has no key header.
    /// </summary>
    private const string ExpectedPath =
        "/API/V1/2fa-secret/SMS/+919876543210/4829/LOGIN_OTP";

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
    public async Task Posts_to_the_documented_route_with_the_key_in_the_path()
    {
        var (provider, handler) = Build();

        var result = await provider.SendOtpAsync("+919876543210", "4829");

        result.Success.Should().BeTrue();
        result.ProviderMessageId.Should().Be("OTP sent successfully to 919876543210");

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].PathAndQuery.Should().Be(ExpectedPath);
    }

    [Fact]
    public async Task Sends_no_api_key_header_and_no_request_body()
    {
        // 2Factor reads the key from the path. A header or body is silently ignored by the gateway,
        // and would put the secret in a place 2Factor never reads it from.
        var (provider, handler) = Build();

        await provider.SendOtpAsync("+919876543210", "4829");

        handler.Requests[0].Headers.Should().NotContainKey("X-API-Key");
        handler.Requests[0].Body.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task Sends_the_generated_code_and_the_registered_template_name()
    {
        var (provider, handler) = Build();

        await provider.SendOtpAsync("+919876543210", "4829");

        // Path segments, not body properties.
        handler.Requests[0].PathAndQuery.Should().Contain("/4829/");
        handler.Requests[0].PathAndQuery.Should().EndWith("/LOGIN_OTP");
    }

    [Fact]
    public async Task Keeps_the_country_code_prefix_unescaped_on_the_destination()
    {
        var (provider, handler) = Build();

        await provider.SendOtpAsync("+919876543210", "4829");

        // %2B would be read by 2Factor as a literal plus rather than a country-code separator.
        handler.Requests[0].PathAndQuery.Should().Contain("/SMS/+919876543210/");
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
    public async Task Reads_the_settings_the_administrator_saved_over_deployment_configuration()
    {
        var (provider, handler) = Build(saved: SmsSettingsSnapshot(
            (SmsSettingNames.ApiKey, "saved-key"),
            (SmsSettingNames.TemplateName, "SAVED_TPL")));

        var result = await provider.SendOtpAsync("+919876543210", "4829");

        result.Success.Should().BeTrue();
        handler.Requests[0].PathAndQuery.Should().Be("/API/V1/saved-key/SMS/+919876543210/4829/SAVED_TPL");
    }

    [Fact]
    public async Task Maps_Status_Success_to_a_send()
    {
        var (provider, _) = Build(HttpStatusCode.OK, Accepted);

        var result = await provider.SendOtpAsync("+919876543210", "4829");

        result.Success.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
    }

    [Fact]
    public async Task A_rejection_arrives_as_200_with_Status_Error_and_is_not_a_send()
    {
        // This is the shape that actually matters: 2Factor reports an invalid key or an unapproved
        // template as 200 OK with "Status":"Error". Treating HTTP status alone as success would
        // have reported a failed send as delivered.
        var (provider, _) = Build(
            HttpStatusCode.OK, """{"Status":"Error","Details":"API Key Invalid"}""");

        var result = await provider.SendOtpAsync("+919876543210", "4829");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("HTTP_200");
        result.Retryable.Should().BeFalse();
        result.ErrorMessage.Should().Be("API Key Invalid", "the provider's own text is the diagnosis");
    }

    [Fact]
    public async Task A_missing_route_is_reported_as_a_bad_request_url()
    {
        // The failure that shipped: posting to /API/V1/OTP/SEND answered 404 for every send.
        var (provider, _) = Build(HttpStatusCode.NotFound, "{}");

        var result = await provider.SendOtpAsync("+919876543210", "4829");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("HTTP_404");
        result.Retryable.Should().BeFalse();
        result.ErrorMessage.Should().Contain("request URL");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task An_unauthorised_key_is_not_retryable(HttpStatusCode status)
    {
        var (provider, _) = Build(status, "{}");

        var result = await provider.SendOtpAsync("+919876543210", "4829");

        result.Success.Should().BeFalse();
        result.Retryable.Should().BeFalse();
        result.ErrorMessage.Should().Contain("API key");
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task A_transient_status_is_retryable(HttpStatusCode status)
    {
        var (provider, _) = Build(status, "{}");

        var result = await provider.SendOtpAsync("+919876543210", "4829");

        result.Success.Should().BeFalse();
        result.Retryable.Should().BeTrue();
    }

    [Fact]
    public async Task An_unparseable_body_is_treated_as_a_send()
    {
        // A shape we have not seen before must not fail a send that the gateway accepted.
        var (provider, _) = Build(HttpStatusCode.OK, "<html>ok</html>");

        var result = await provider.SendOtpAsync("+919876543210", "4829");

        result.Success.Should().BeTrue();
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

    private static SmsProviderSettingsSnapshot SmsSettingsSnapshot(params (string Key, string Value)[] values)
    {
        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in values)
            settings[key] = value;

        return new SmsProviderSettingsSnapshot(true, SmsProviderKind.TwoFactor, settings);
    }
}