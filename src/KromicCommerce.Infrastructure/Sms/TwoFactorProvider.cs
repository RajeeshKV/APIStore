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
/// Sends the OTP code that this application generates through 2Factor's dedicated OTP endpoint:
/// <c>POST https://2factor.in/API/V1/OTP/SEND</c>, authenticated with an <c>X-API-Key</c> header
/// and a <c>{ to, channel, template_name, var1 }</c> body. The template name and API key are
/// resolved from the administrator's saved settings or deployment configuration.
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

        var payload = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["to"] = destination,
            ["channel"] = "SMS",
            ["template_name"] = templateName,
            ["var1"] = otp
        };

        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var client = httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                new Uri("https://2factor.in/API/V1/OTP/SEND"))
            {
                Content = System.Net.Http.Json.JsonContent.Create(payload, options: SmsJson.Options)
            };

            request.Headers.TryAddWithoutValidation("X-API-Key", apiKey);

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();

            if (response.IsSuccessStatusCode && IsAccepted(body))
            {
                var sessionId = ReadString(body, "session_id") ?? ReadString(body, "SessionId");

                logger.LogInformation("2Factor accepted the OTP. Session: {SessionId}. DurationMs: {DurationMs}",
                    sessionId, stopwatch.ElapsedMilliseconds);

                return new SmsSendResult(true, sessionId, null, null, false);
            }

            var code = ReadString(body, "code") ?? $"HTTP_{(int)response.StatusCode}";
            var retryable = IsRetryable(code, response.StatusCode);

            logger.LogWarning(
                "2Factor rejected the OTP. Code: {Code}, HTTP {StatusCode}, retryable: {Retryable}. DurationMs: {DurationMs}",
                code, (int)response.StatusCode, retryable, stopwatch.ElapsedMilliseconds);

            return new SmsSendResult(false, null, code, DescribeError(code), retryable);
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
    /// 2Factor's published examples answer <c>{ "status": "sent" }</c>. A body with no status at
    /// all is treated as accepted, but an explicit non-success status is not.
    /// </summary>
    private static bool IsAccepted(string body)
    {
        var status = ReadString(body, "status") ?? ReadString(body, "Status");

        if (status is null)
            return true;

        return status.Equals("sent", StringComparison.OrdinalIgnoreCase)
               || status.Equals("success", StringComparison.OrdinalIgnoreCase)
               || status.Equals("ok", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRetryable(string code, HttpStatusCode status) => code switch
    {
        "RATE_LIMITED" => true,
        "GATEWAY_UNAVAILABLE" => true,
        _ => (int)status >= 500 || status == HttpStatusCode.TooManyRequests
              || status == HttpStatusCode.RequestTimeout
    };

    private static string DescribeError(string code) => code switch
    {
        "INVALID_API_KEY" or "UNAUTHORIZED" => "The 2Factor API key is not valid.",
        "INVALID_TEMPLATE" or "TEMPLATE_NOT_FOUND" => "The configured 2Factor template is not approved.",
        "TEMPLATE_MISMATCH" => "The template variables do not match the approved template.",
        "INVALID_NUMBER" or "INVALID_PHONE_NUMBER" => "The destination number is not valid.",
        "NO_VALID_NUMBERS" => "No valid mobile number was parsed from the request.",
        "RATE_LIMITED" => "Rate limit exceeded. Retry after the indicated delay.",
        "GATEWAY_UNAVAILABLE" => "The 2Factor gateway is temporarily unavailable.",
        _ => $"2Factor rejected the message ({code})."
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
