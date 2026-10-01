using System.Net.Http.Json;
using KromicCommerce.Application.Abstractions.Sms;
using KromicCommerce.Infrastructure.Configuration;

namespace KromicCommerce.Infrastructure.Sms;

/// <summary>
/// 2Factor (2factor.in) adapter, using the template-driven transactional send.
/// </summary>
/// <remarks>
/// <para>
/// <b>Verify this before production use.</b> Unlike the other two gateways, 2Factor's published
/// API reference could not be fetched — its documentation routes are JavaScript-rendered or
/// return 403/404. The request shape here follows the template send described in 2Factor's own
/// product documentation, and every path segment and variable name is configuration
/// (<see cref="TwoFactorOptions.SendPath"/>, <see cref="TwoFactorOptions.OtpVariableName"/>, …)
/// rather than a hard-coded constant. If your account's reference differs, correct the values in
/// configuration; no rebuild is involved. A wrong path surfaces as a normal, reported send
/// failure and can never take the API down or expose the API key.
/// </para>
/// <para>
/// A hosted-OTP integration was rejected deliberately: it would hand code generation and
/// verification to the vendor, and customers would then see different expiry, attempt limits and
/// resend cooldowns depending on the active gateway. Sending our own code through a registered
/// template keeps those rules identical for every provider.
/// </para>
/// </remarks>
internal sealed class TwoFactorProvider(
    IOptions<TwoFactorOptions> options,
    IOptions<SmsOtpPolicyOptions> otpPolicy,
    SmsProviderSettingsSnapshot? saved,
    ISmsTemplateStore templates,
    IHttpClientFactory httpClientFactory,
    ILogger<TwoFactorProvider> logger) : ISmsProvider
{
    public const string HttpClientName = "TwoFactor";

    public string ProviderName => "2Factor";

    public SmsProviderKind Kind => SmsProviderKind.TwoFactor;

    public async Task<SmsSendResult> SendOtpAsync(
        string phoneNumber, string otp, CancellationToken cancellationToken = default)
    {
        if (!TryToInternational(phoneNumber, out var destination))
        {
            return new SmsSendResult(false, null, "INVALID_PHONE_NUMBER",
                "The phone number is not a valid international number.", false);
        }

        var configured = options.Value;
        var apiKey = SmsSettings.Resolve(saved, "ApiKey", configured.ApiKey);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogError("2Factor is selected but ApiKey is missing.");
            return new SmsSendResult(false, null, "MISSING_CREDENTIALS",
                "2Factor credentials are not configured.", false);
        }

        var baseUrl = SmsSettings.Resolve(saved, "BaseUrl", configured.BaseUrl) ?? configured.BaseUrl;
        var sendPath = SmsSettings.Resolve(saved, "SendPath", configured.SendPath) ?? configured.SendPath;
        var senderId = SmsSettings.Resolve(saved, "SenderId", configured.SenderId);
        var otpVariable = SmsSettings.Resolve(saved, "OtpVariableName", configured.OtpVariableName)
                          ?? configured.OtpVariableName;
        var expiryVariable = SmsSettings.Resolve(saved, "ExpiryVariableName", configured.ExpiryVariableName)
                             ?? configured.ExpiryVariableName;

        var template = await templates.GetActiveAsync(SmsProviderKind.TwoFactor, cancellationToken);
        if (template is not { HasExternalTemplate: true })
        {
            // 2Factor routes transactional traffic through an approved DLT template, so there is
            // no sensible message to send without one. Failing here is better than a send that
            // the gateway rejects — and it tells the administrator exactly what is missing.
            logger.LogError(
                "2Factor is selected but no active template with a registered template ID exists.");
            return new SmsSendResult(false, null, "TEMPLATE_NOT_CONFIGURED",
                "2Factor requires an approved message template to be configured before it can send.", false);
        }

        var payload = new Dictionary<string, string>
        {
            ["apiKey"] = apiKey,
            ["to"] = destination,
            ["templateId"] = template.ExternalTemplateId!,
            [otpVariable] = otp,
            [expiryVariable] =
                otpPolicy.Value.ClampedExpiryMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        if (!string.IsNullOrWhiteSpace(senderId))
            payload["senderId"] = senderId;

        try
        {
            using var client = httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(
                HttpMethod.Post, SmsEndpoint.Build(baseUrl, sendPath))
            {
                Content = JsonContent.Create(payload, options: SmsJson.Options)
            };

            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode && IsAccepted(body))
            {
                var sessionId = ReadString(body, "SessionId") ?? ReadString(body, "SessionID");
                logger.LogInformation("2Factor accepted the OTP. Session: {SessionId}", sessionId);
                return new SmsSendResult(true, sessionId, null, null, false);
            }

            var code = ReadString(body, "Status") ?? ReadInt(body, "code")?.ToString(
                System.Globalization.CultureInfo.InvariantCulture) ?? $"HTTP_{(int)response.StatusCode}";

            var retryable = (int)response.StatusCode >= 500 || code is "RATE_LIMITED" or "GATEWAY_UNAVAILABLE";

            logger.LogWarning("2Factor rejected the OTP. Code: {Code}, HTTP {StatusCode}", code, (int)response.StatusCode);
            return new SmsSendResult(false, null, code, DescribeError(code), retryable);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "2Factor HTTP request failed");
            return new SmsSendResult(false, null, "HTTP_ERROR", "Could not reach 2Factor.", true);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError("2Factor request timed out");
            return new SmsSendResult(false, null, "TIMEOUT", "Provider request timed out.", true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected 2Factor error");
            return new SmsSendResult(false, null, "UNEXPECTED_ERROR", "Unexpected provider error.", false);
        }
    }

    /// <summary>
    /// 2Factor answers with a <c>{"Status": "...", "Details": "..."}</c> envelope and has
    /// historically used HTTP 200 for application-level failures. A 2xx with no
    /// <c>Status</c> field is therefore treated as accepted, but an explicit non-success status
    /// is not — accepting one would silently report an undelivered code as sent.
    /// </summary>
    private static bool IsAccepted(string body)
    {
        var status = ReadString(body, "Status");
        return status is null || status.Equals("Success", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryToInternational(string phoneNumber, out string international)
    {
        international = string.Empty;

        if (string.IsNullOrWhiteSpace(phoneNumber))
            return false;

        var trimmed = phoneNumber.Trim();
        if (!trimmed.StartsWith('+'))
            return false;

        var digits = trimmed[1..].Where(char.IsAsciiDigit).ToArray();
        if (digits.Length is < 7 or > 15)
            return false;

        international = "+" + new string(digits);
        return true;
    }

    private static string? ReadString(string json, string property)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(property, out var value)
                   && value.ValueKind == System.Text.Json.JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static int? ReadInt(string json, string property)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(property, out var value)
                   && value.ValueKind == System.Text.Json.JsonValueKind.Number
                   && value.TryGetInt32(out var n)
                ? n
                : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static string DescribeError(string code) => code switch
    {
        "Error" or "ERROR" => "2Factor rejected the request.",
        "INVALID_API_KEY" => "The 2Factor API key is not valid.",
        "INVALID_TEMPLATE" => "The configured 2Factor template is not approved for this account.",
        "TEMPLATE_MISMATCH" => "The message variables do not match the approved template.",
        "INVALID_NUMBER" => "The destination number is not valid.",
        "RATE_LIMITED" => "Rate limit exceeded. Retry after the indicated delay.",
        "GATEWAY_UNAVAILABLE" => "The 2Factor gateway is temporarily unavailable.",
        _ => $"2Factor rejected the message ({code})."
    };
}
