using System.Net;
using System.Net.Http.Headers;
using System.Text;
using KromicCommerce.Application.Abstractions.Sms;
using KromicCommerce.Infrastructure.Configuration;

namespace KromicCommerce.Infrastructure.Sms;

/// <summary>
/// Twilio Verify adapter (Verify v2 REST API).
/// </summary>
/// <remarks>
/// <para>
/// Reference: https://www.twilio.com/docs/verify/api/verification
/// <code>
/// POST {BaseUrl}/Services/{ServiceSid}/Verifications
/// Authorization: Basic base64({AccountSid}:{AuthToken})
/// Content-Type: application/x-www-form-urlencoded
///
///   To={E164}                 required
///   Channel=sms               required
///   CustomCode={OTP}          set by this adapter
///   TemplateSid={HJ...}       when a Verify template is configured
///   TemplateCustomSubstitutions={...}  when substitutions are configured
/// </code>
/// </para>
/// <para>
/// <c>CustomCode</c> is what keeps this application authoritative for code generation, hashing,
/// expiry, attempt limits and cooldown. Letting Twilio generate the code instead would move
/// verification to Twilio's API, and a customer would then face different attempt limits,
/// resend cooldowns and failure messages depending on which gateway the store happens to run —
/// exactly the inconsistency provider selection is meant to avoid.
/// </para>
/// <para>
/// Twilio does not confirm a delivered code, so a successful send is reported with
/// <c>pending</c> status and the caller still has to run its own verification.
/// </para>
/// </remarks>
internal sealed class TwilioProvider(
    IOptions<TwilioOptions> options,
    IOptions<SmsOtpPolicyOptions> otpPolicy,
    SmsProviderSettingsSnapshot? saved,
    ISmsTemplateStore templates,
    IHttpClientFactory httpClientFactory,
    ILogger<TwilioProvider> logger) : ISmsProvider
{
    public const string HttpClientName = "Twilio";

    public string ProviderName => "Twilio";

    public SmsProviderKind Kind => SmsProviderKind.Twilio;

    public async Task<SmsSendResult> SendOtpAsync(
        string phoneNumber, string otp, CancellationToken cancellationToken = default)
    {
        if (!TryToE164(phoneNumber, out var e164))
        {
            return new SmsSendResult(false, null, "INVALID_PHONE_NUMBER",
                "The phone number is not a valid international number.", false);
        }

        var configured = options.Value;
        var accountSid = SmsSettings.Resolve(saved, "AccountSid", configured.AccountSid);
        var authToken = SmsSettings.Resolve(saved, "AuthToken", configured.AuthToken);
        var serviceSid = SmsSettings.Resolve(saved, "ServiceSid", configured.ServiceSid);

        if (string.IsNullOrWhiteSpace(accountSid)
            || string.IsNullOrWhiteSpace(authToken)
            || string.IsNullOrWhiteSpace(serviceSid))
        {
            logger.LogError("Twilio is selected but AccountSid, AuthToken or ServiceSid is missing.");
            return new SmsSendResult(false, null, "MISSING_CREDENTIALS",
                "Twilio credentials are not configured.", false);
        }

        var baseUrl = SmsSettings.Resolve(saved, "BaseUrl", configured.BaseUrl) ?? configured.BaseUrl;
        var messagingServiceSid = SmsSettings.Resolve(saved, "MessagingServiceSid", configured.MessagingServiceSid);

        var template = await templates.GetActiveAsync(SmsProviderKind.Twilio, cancellationToken);

        var form = new Dictionary<string, string>
        {
            ["To"] = e164,
            ["Channel"] = "sms",
            // Our code, not Twilio's — see remarks.
            ["CustomCode"] = otp
        };

        if (template is { HasExternalTemplate: true })
        {
            form["TemplateSid"] = template.ExternalTemplateId!;

            if (template.HasBody)
            {
                // When a Verify template is used the body defines the substitution keys, so the
                // OTP has to be passed as a custom substitution rather than as CustomCode.
                form.Remove("CustomCode");
                form["TemplateCustomSubstitutions"] = BuildSubstitutions(
                    template, otp, otpPolicy.Value.ClampedExpiryMinutes);
            }
        }

        if (!string.IsNullOrWhiteSpace(messagingServiceSid))
            form["MessagingServiceSid"] = messagingServiceSid;

        try
        {
            using var client = httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                SmsEndpoint.Build(baseUrl, $"Services/{serviceSid}/Verifications"))
            {
                Content = new FormUrlEncodedContent(form)
            };

            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{accountSid}:{authToken}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var sid = ReadString(body, "sid");
                var status = ReadString(body, "status");
                logger.LogInformation("Twilio accepted the OTP. SID: {Sid}, status: {Status}", sid, status);
                return new SmsSendResult(true, sid, null, null, false);
            }

            var twilioCode = ReadInt(body, "code") is { } numeric
                ? numeric.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : $"HTTP_{(int)response.StatusCode}";

            // 20404 means the destination is not on a trial account / not verified. Sending to a
            // different number is a genuine retry, so it is the one Twilio 4xx treated as such.
            var retryable = (int)response.StatusCode >= 500 || twilioCode == "20404";

            logger.LogWarning("Twilio rejected the OTP. Code: {Code}, HTTP {StatusCode}", twilioCode, (int)response.StatusCode);
            return new SmsSendResult(false, null, twilioCode, DescribeError(twilioCode), retryable);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Twilio HTTP request failed");
            return new SmsSendResult(false, null, "HTTP_ERROR", "Could not reach Twilio.", true);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError("Twilio request timed out");
            return new SmsSendResult(false, null, "TIMEOUT", "Provider request timed out.", true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected Twilio error");
            return new SmsSendResult(false, null, "UNEXPECTED_ERROR", "Unexpected provider error.", false);
        }
    }

    private static string BuildSubstitutions(SmsTemplateSnapshot template, string otp, int expiryMinutes)
    {
        // Twilio expects a JSON object of string values whose keys are the {tokens} in the
        // registered template body. Deriving the keys from the body keeps the admin-facing
        // template definition identical to the one used for Free2SMS, rather than a second,
        // provider-specific vocabulary the administrator would have to learn.
        var substitutions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["OTP"] = otp,
            ["EXPIRY_MINUTES"] = expiryMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        var placeholder = System.Text.RegularExpressions.Regex.Matches(template.Body, @"\{([A-Z0-9_]+)\}");
        foreach (System.Text.RegularExpressions.Match match in placeholder)
        {
            var key = match.Groups[1].Value;
            if (!substitutions.ContainsKey(key))
                substitutions[key] = string.Empty;
        }

        return System.Text.Json.JsonSerializer.Serialize(substitutions);
    }

    private static bool TryToE164(string phoneNumber, out string e164)
    {
        e164 = string.Empty;

        if (string.IsNullOrWhiteSpace(phoneNumber))
            return false;

        var trimmed = phoneNumber.Trim();
        if (trimmed.StartsWith('+'))
        {
            var digits = trimmed[1..].Where(char.IsAsciiDigit).ToArray();
            // E.164 allows at most 15 digits and no fewer than 7 for a real subscriber number.
            if (digits.Length is < 7 or > 15)
                return false;

            e164 = "+" + new string(digits);
            return true;
        }

        // A bare national number has no country code, and Twilio will not guess one. A default
        // dial code would send codes to the wrong country, so this is refused rather than
        // assumed — the caller must pass an international number.
        return false;
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
            if (!document.RootElement.TryGetProperty(property, out var value))
                return null;

            return value.ValueKind == System.Text.Json.JsonValueKind.Number && value.TryGetInt32(out var n)
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
        "20001" => "The request to Twilio could not be completed.",
        "20003" => "The Twilio authentication token is not valid.",
        "20005" => "The requested Verify Service does not exist.",
        "20404" => "The destination number is not reachable or not verified for this account.",
        "20406" => "The destination number is not valid.",
        "21211" => "The destination number is not a valid mobile number.",
        "21212" => "The From / Messaging Service number is not valid.",
        "21214" => "The To number is not a valid mobile number.",
        "60200" => "Twilio could not route the message to any carrier for that number.",
        "60202" => "The destination number is not mobile-capable and cannot receive SMS.",
        "60600" => "The destination number is not reachable from the Twilio account's country.",
        _ => $"Twilio rejected the message ({code})."
    };
}
