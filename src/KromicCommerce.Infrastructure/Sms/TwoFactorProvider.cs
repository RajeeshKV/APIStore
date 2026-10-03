using System.Diagnostics;
using System.Net;
using System.Text.Json;
using KromicCommerce.Application.Abstractions.Sms;
using KromicCommerce.Domain.Sms;
using KromicCommerce.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KromicCommerce.Infrastructure.Sms;

/// <summary>
/// 2Factor (2factor.in) adapter.
/// </summary>
/// <remarks>
/// <para>
/// Sends the OTP code that this application generates through 2Factor's Manual OTP API:
/// <c>POST https://2factor.in/API/V1/{api_key}/SMS/{phone_number}/{otp_code}/{template_name}</c>.
/// </para>
/// <para>
/// <b>Everything identifying travels in the path, not the body.</b> 2Factor has no
/// <c>X-API-Key</c> header and no JSON request body: the API key, the destination, the code and
/// the registered template name are all URL path segments. An earlier revision of this adapter
/// posted to <c>/API/V1/OTP/SEND</c> with an <c>X-API-Key</c> header and a
/// <c>{to, channel, template_name, var1}</c> JSON body, which answered <c>404</c> for every send —
/// that endpoint does not exist.
/// </para>
/// <para>
/// The response is <c>{"Status":"Success","Details":"…"}</c>, and an application-level rejection
/// also arrives as <c>200 OK</c> with <c>"Status":"Error"</c> plus a prose <c>Details</c>. So HTTP
/// status alone cannot decide success, and there is no machine-readable error code to switch on —
/// the provider's own <c>Details</c> text is surfaced verbatim.
/// </para>
/// <para>
/// 2Factor does not generate, store, or verify the OTP — it only delivers the code this application
/// passes to it. Expiry, attempt limits, and resend cooldown are all owned by the application.
/// </para>
/// </remarks>
internal sealed class TwoFactorProvider(
    IOptions<TwoFactorOptions> options,
    SmsProviderSettingsSnapshot? saved,
    IHttpClientFactory httpClientFactory,
    ILogger<TwoFactorProvider> logger) : ISmsProvider
{
    public const string HttpClientName = "TwoFactor";

    private const string BaseUrl = "https://2factor.in/API/V1";

    public string ProviderName => "2Factor";

    public SmsProviderKind Kind => SmsProviderKind.TwoFactor;

    public async Task<SmsSendResult> SendOtpAsync(
        string phoneNumber, string otp, CancellationToken cancellationToken = default)
    {
        if (SmsPhoneNumber.TryToE164(phoneNumber) is not { } destination)
        {
            logger.LogWarning("2Factor rejected an OTP send: the destination is not a valid Indian mobile number.");
            return new SmsSendResult(false, null, "INVALID_PHONE_NUMBER",
                "The phone number is not a valid 10-digit mobile number.", false);
        }

        var configured = options.Value;
        var apiKey = SmsSettings.Resolve(saved, SmsSettingNames.ApiKey, configured.ApiKey);
        var templateName = SmsSettings.Resolve(saved, SmsSettingNames.TemplateName, configured.TemplateName);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogError("2Factor is selected but ApiKey is missing.");
            return new SmsSendResult(false, null, "MISSING_CREDENTIALS",
                "2Factor credentials are not configured.", false);
        }

        if (string.IsNullOrWhiteSpace(templateName))
        {
            logger.LogError("2Factor is selected but no template name is configured.");
            return new SmsSendResult(false, null, "MISSING_TEMPLATE",
                "A 2Factor template name must be configured before sending.", false);
        }

        // The destination is inserted unescaped on purpose: E.164's leading '+' is a legal path
        // character, and percent-encoding it (%2B) risks the provider reading it as a literal plus
        // rather than a country-code separator. The other segments are attacker-influenced only
        // through saved admin settings, but are escaped regardless.
        var uri = new Uri(
            $"{BaseUrl}/{Uri.EscapeDataString(apiKey)}/SMS/{destination}/" +
            $"{Uri.EscapeDataString(otp)}/{Uri.EscapeDataString(templateName)}");

        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var client = httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, uri);

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();

            var details = ReadString(body, "Details") ?? ReadString(body, "details");

            // A rejected send is a 200 with "Status":"Error", so the body decides — but only when
            // there is a body. A non-2xx is always a failure regardless of what it says.
            if (response.IsSuccessStatusCode && IsAccepted(body))
            {
                logger.LogInformation(
                    "2Factor accepted the OTP. Details: {Details}. DurationMs: {DurationMs}",
                    details, stopwatch.ElapsedMilliseconds);

                return new SmsSendResult(true, details, null, null, false);
            }

            var code = $"HTTP_{(int)response.StatusCode}";
            var rejected = response.IsSuccessStatusCode;
            var retryable = !rejected && IsRetryable(response.StatusCode);

            // Never log the code or the API key, only what 2Factor said about them.
            logger.LogWarning(
                "2Factor rejected the OTP. Code: {Code}, HTTP {StatusCode}, retryable: {Retryable}. " +
                "DurationMs: {DurationMs}",
                code, (int)response.StatusCode, retryable, stopwatch.ElapsedMilliseconds);

            var message = string.IsNullOrWhiteSpace(details)
                ? rejected
                    ? "2Factor rejected the message."
                    : DescribeError(response.StatusCode)
                : details;

            return new SmsSendResult(false, null, code, message, retryable);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "2Factor HTTP request failed. DurationMs: {DurationMs}", stopwatch.ElapsedMilliseconds);
            return new SmsSendResult(false, null, "HTTP_ERROR", "Could not reach 2Factor.", true);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError("2Factor request timed out. DurationMs: {DurationMs}", stopwatch.ElapsedMilliseconds);
            return new SmsSendResult(false, null, "TIMEOUT", "Provider request timed out.", true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected 2Factor error.");
            return new SmsSendResult(false, null, "UNEXPECTED_ERROR", "Unexpected provider error.", false);
        }
    }

    /// <summary>
    /// 2Factor answers <c>{"Status":"Success"}</c>, and reports an application-level rejection as
    /// <c>{"Status":"Error"}</c> under a 200. A body with no recognisable status is accepted, since
    /// that is a successful send on a shape we have not seen before.
    /// </summary>
    private static bool IsAccepted(string body)
    {
        var status = ReadString(body, "Status") ?? ReadString(body, "status");

        if (status is null)
            return true;

        return status.Equals("Success", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRetryable(HttpStatusCode status) =>
        (int)status >= 500
        || status == HttpStatusCode.TooManyRequests
        || status == HttpStatusCode.RequestTimeout;

    private static string DescribeError(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
            "The 2Factor API key is not valid.",
        HttpStatusCode.NotFound =>
            "2Factor rejected the request URL. Check the API key and template name.",
        HttpStatusCode.TooManyRequests =>
            "Rate limit exceeded. Retry after the indicated delay.",
        _ => $"2Factor could not be reached (HTTP {(int)status})."
    };

    private static string? ReadString(string json, string property)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(property, out var value)
                   && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
