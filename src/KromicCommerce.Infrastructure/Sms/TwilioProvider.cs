using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using KromicCommerce.Application.Abstractions.Sms;
using KromicCommerce.Domain.Sms;
using KromicCommerce.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KromicCommerce.Infrastructure.Sms;

/// <summary>
/// Twilio adapter. Verify v2 is Twilio's dedicated OTP endpoint and is preferred; Programmable
/// Messaging is the transactional fallback.
/// </summary>
/// <remarks>
/// <para>
/// References:
/// <list type="bullet">
/// <item>https://www.twilio.com/docs/verify/api/verification</item>
/// <item>https://www.twilio.com/docs/verify/verification-templates</item>
/// </list>
/// </para>
/// <para>
/// <b>Native route.</b> <c>POST {BaseUrl}/Services/{ServiceSid}/Verifications</c>, Basic auth as
/// <c>{AccountSid}:{AuthToken}</c>, form-encoded:
/// <c>To</c>, <c>Channel=sms</c>, <c>CustomCode</c>, and <c>TemplateSid</c> when the
/// administrator has registered one.
/// </para>
/// <para>
/// <b>Template selection follows Twilio's documented precedence:</b> a <c>TemplateSid</c> on the
/// request wins, then the Service's <c>DefaultTemplateSid</c>, then the Verify Default template.
/// Supplying the request-level SID is therefore how a per-deployment template is chosen, which is
/// why it is read from the active template on every send rather than configured once.
/// </para>
/// <para>
/// <b>Our code, not Twilio's.</b> <c>CustomCode</c> keeps this application authoritative for code
/// generation, hashing, expiry, attempt limits and cooldown. Letting Twilio generate the code
/// would move verification to Twilio's Verification Check API, and a customer would then meet
/// different attempt limits and resend cooldowns depending on the active gateway. It requires
/// <c>CustomCodeEnabled</c> on the Service. A raw custom message body is <b>not</b> sent —
/// Twilio rejects it with error 60243; template variables go through
/// <c>TemplateCustomSubstitutions</c> instead.
/// </para>
/// </remarks>
internal sealed class TwilioProvider(
    IOptions<TwilioOptions> options,
    IOptions<SmsOtpPolicyOptions> otpPolicy,
    SmsProviderSettingsSnapshot? saved,
    ISmsTemplateStore templates,
    ISmsOtpAuditSink audit,
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
            logger.LogWarning("Twilio rejected an OTP send: the destination is not a valid E.164 number.");
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

        var expiryMinutes = otpPolicy.Value.ClampedExpiryMinutes;

        // Read from the administrator's configuration on every request, so activating a different
        // Verify template applies to the very next OTP.
        var template = await SmsTemplateResolver
            .ResolveAsync(templates, SmsProviderKind.Twilio, otp, expiryMinutes, cancellationToken)
            .ConfigureAwait(false);

        var mode = ResolveDeliveryMode(configured);

        logger.LogDebug(
            "Twilio OTP send starting. Mode: {Mode}. Template: {TemplateName} (ref {TemplateReference}).",
            mode.ToName(),
            template.Exists ? template.AdminName : "(none)",
            template.Reference ?? "(none)");

        return mode == SmsOtpDeliveryMode.TransactionalTemplate
            ? await SendTransactionalAsync(
                new SendContext(accountSid, authToken, serviceSid, e164, otp, expiryMinutes, template),
                cancellationToken)
            : await SendVerifyAsync(
                new SendContext(accountSid, authToken, serviceSid, e164, otp, expiryMinutes, template),
                cancellationToken);
    }

    /// <summary>Twilio Verify — the native OTP endpoint.</summary>
    private async Task<SmsSendResult> SendVerifyAsync(SendContext context, CancellationToken cancellationToken)
    {
        var configured = options.Value;
        var baseUrl = Resolve("BaseUrl", configured.BaseUrl) ?? configured.BaseUrl;
        var messagingServiceSid = SmsSettings.Resolve(saved, "MessagingServiceSid", configured.MessagingServiceSid);

        var form = new Dictionary<string, string>
        {
            ["To"] = context.E164,
            ["Channel"] = "sms"
        };

        if (context.Template.HasReference)
        {
            // Highest-precedence override in Twilio's documented template precedence.
            form["TemplateSid"] = context.Template.Reference!;

            if (context.Template.HasBody)
            {
                // A raw body is rejected (60243); the code is passed as a substitution against the
                // tokens in the registered template instead.
                form["TemplateCustomSubstitutions"] = BuildSubstitutions(context.Template);
            }
            else
            {
                form["CustomCode"] = context.Otp;
            }
        }
        else
        {
            form["CustomCode"] = context.Otp;
        }

        if (!string.IsNullOrWhiteSpace(messagingServiceSid))
            form["MessagingServiceSid"] = messagingServiceSid;

        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var client = httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                SmsEndpoint.Build(baseUrl, $"Services/{context.ServiceSid}/Verifications"))
            {
                Content = new FormUrlEncodedContent(form)
            };

            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{context.AccountSid}:{context.AuthToken}")));

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            SmsSendResult result;

            if (response.IsSuccessStatusCode)
            {
                var sid = ReadString(body, "sid");
                var status = ReadString(body, "status");
                logger.LogInformation("Twilio accepted the OTP via Verify. SID: {Sid}, status: {Status}", sid, status);
                result = new SmsSendResult(true, sid, null, null, false);
            }
            else
            {
                var twilioCode = ReadInt(body, "code")?.ToString(CultureInfo.InvariantCulture)
                                 ?? $"HTTP_{(int)response.StatusCode}";

                // 20404 means the destination is not on a trial account / not verified. Sending to a
                // different number is a genuine retry, so it is the one 4xx treated as retryable.
                var retryable = (int)response.StatusCode >= 500 || twilioCode == "20404";

                logger.LogWarning(
                    "Twilio rejected the OTP via Verify. Code: {Code}, HTTP {StatusCode}, retryable: {Retryable}.",
                    twilioCode, (int)response.StatusCode, retryable);
                result = new SmsSendResult(false, null, twilioCode, DescribeError(twilioCode), retryable);
            }

            Publish(context, "native", SmsOtpDeliveryMode.NativeOtp, result, stopwatch);
            return result;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Twilio Verify HTTP request failed.");
            var result = new SmsSendResult(false, null, "HTTP_ERROR", "Could not reach Twilio.", true);
            Publish(context, "native", SmsOtpDeliveryMode.NativeOtp, result, stopwatch);
            return result;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError("Twilio Verify request timed out.");
            var result = new SmsSendResult(false, null, "TIMEOUT", "Provider request timed out.", true);
            Publish(context, "native", SmsOtpDeliveryMode.NativeOtp, result, stopwatch);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected Twilio Verify error.");
            var result = new SmsSendResult(false, null, "UNEXPECTED_ERROR", "Unexpected provider error.", false);
            Publish(context, "native", SmsOtpDeliveryMode.NativeOtp, result, stopwatch);
            return result;
        }
    }

    /// <summary>
    /// Programmable Messaging fallback. Used only when explicitly configured, because it drops
    /// back to a self-built send and forfeits Verify's built-in rate limiting and Fraud Guard.
    /// </summary>
    private async Task<SmsSendResult> SendTransactionalAsync(
        SendContext context, CancellationToken cancellationToken)
    {
        var configured = options.Value;
        var messagingBaseUrl = Resolve("MessagingBaseUrl", configured.MessagingBaseUrl)
                               ?? configured.MessagingBaseUrl;
        // {accountSid} is expanded from the effective credential whether the path came from
        // configuration or from the administrator's saved settings.
        var messagingPath = (Resolve("MessagingPath", configured.MessagingPath) ?? configured.MessagingPath)
            .Replace("{accountSid}", Uri.EscapeDataString(context.AccountSid), StringComparison.Ordinal);

        var messagingServiceSid = SmsSettings.Resolve(saved, "MessagingServiceSid", configured.MessagingServiceSid);
        var senderId = Resolve("SenderId", configured.SenderId) ?? messagingServiceSid;

        // Twilio requires either a From or a MessagingServiceSid; without one the send is rejected.
        if (string.IsNullOrWhiteSpace(senderId))
        {
            logger.LogError(
                "Twilio is set to the transactional fallback but neither SenderId nor " +
                "MessagingServiceSid is configured.");
            return new SmsSendResult(false, null, "MISSING_CREDENTIALS",
                "The Twilio transactional fallback requires a sender.", false);
        }

        var body = context.Template.HasBody
            ? context.Template.Render()
            : $"Your verification code is {context.Otp}. "
              + $"It expires in {context.ExpiryMinutes.ToString(CultureInfo.InvariantCulture)} minutes.";

        var form = new Dictionary<string, string>
        {
            ["To"] = context.E164,
            ["Body"] = body
        };

        if (!string.IsNullOrWhiteSpace(messagingServiceSid))
            form["MessagingServiceSid"] = messagingServiceSid;
        else
            form["From"] = senderId!;

        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var client = httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(
                HttpMethod.Post, SmsEndpoint.Build(messagingBaseUrl, messagingPath))
            {
                Content = new FormUrlEncodedContent(form)
            };

            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{context.AccountSid}:{context.AuthToken}")));

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            SmsSendResult result;

            if (response.IsSuccessStatusCode)
            {
                var sid = ReadString(responseBody, "sid");
                logger.LogInformation("Twilio accepted the OTP via Programmable Messaging. SID: {Sid}", sid);
                result = new SmsSendResult(true, sid, null, null, false);
            }
            else
            {
                var twilioCode = ReadInt(responseBody, "code")?.ToString(CultureInfo.InvariantCulture)
                                 ?? $"HTTP_{(int)response.StatusCode}";
                var retryable = (int)response.StatusCode >= 500;

                logger.LogWarning(
                    "Twilio rejected the OTP via Programmable Messaging. Code: {Code}, HTTP {StatusCode}.",
                    twilioCode, (int)response.StatusCode);
                result = new SmsSendResult(false, null, twilioCode, DescribeError(twilioCode), retryable);
            }

            Publish(context, "transactional", SmsOtpDeliveryMode.TransactionalTemplate, result, stopwatch);
            return result;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Twilio Programmable Messaging HTTP request failed.");
            var result = new SmsSendResult(false, null, "HTTP_ERROR", "Could not reach Twilio.", true);
            Publish(context, "transactional", SmsOtpDeliveryMode.TransactionalTemplate, result, stopwatch);
            return result;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError("Twilio Programmable Messaging request timed out.");
            var result = new SmsSendResult(false, null, "TIMEOUT", "Provider request timed out.", true);
            Publish(context, "transactional", SmsOtpDeliveryMode.TransactionalTemplate, result, stopwatch);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected Twilio Programmable Messaging error.");
            var result = new SmsSendResult(false, null, "UNEXPECTED_ERROR", "Unexpected provider error.", false);
            Publish(context, "transactional", SmsOtpDeliveryMode.TransactionalTemplate, result, stopwatch);
            return result;
        }
    }

    /// <summary>
    /// JSON object of string values whose keys are the <c>{TOKENS}</c> in the registered template
    /// body. Deriving the keys from the body keeps the admin-facing template definition identical
    /// across providers instead of introducing a second, Twilio-only vocabulary.
    /// </summary>
    private static string BuildSubstitutions(SmsTemplateResolution template)
    {
        var substitutions = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var pair in template.Variables)
        {
            // Strip the braces: Twilio addresses substitution keys by bare name.
            var key = pair.Key.Trim('{', '}');
            if (!string.IsNullOrWhiteSpace(key))
                substitutions[key] = pair.Value;
        }

        if (!substitutions.ContainsKey("OTP"))
            substitutions["OTP"] = string.Empty;

        return System.Text.Json.JsonSerializer.Serialize(substitutions);
    }

    private void Publish(
        SendContext context, string route, SmsOtpDeliveryMode mode, SmsSendResult result, Stopwatch stopwatch)
    {
        stopwatch.Stop();
        audit.Record(new SmsOtpAudit(
            Provider: ProviderName,
            Mode: mode.ToName(),
            AttemptedRoute: route,
            UsedFallback: route == "transactional",
            TemplateName: context.Template.Exists ? context.Template.AdminName : null,
            TemplateReference: context.Template.Reference,
            MaskedPhone: SmsPhoneNumber.Mask(context.E164),
            Success: result.Success,
            ProviderMessageId: result.ProviderMessageId,
            ErrorCode: result.ErrorCode,
            Retryable: result.Retryable,
            DurationMs: stopwatch.ElapsedMilliseconds));
    }

    private SmsOtpDeliveryMode ResolveDeliveryMode(TwilioOptions configured)
    {
        var raw = SmsSettings.Resolve(saved, "DeliveryMode", configured.DeliveryMode.ToName());
        var parsed = SmsOtpDeliveryModes.Parse(raw);

        if (raw is not null && parsed is null)
            logger.LogWarning(
                "Twilio DeliveryMode '{Value}' is not recognised; using {Default}.",
                raw, SmsOtpDeliveryMode.NativeOtp);

        return parsed ?? configured.DeliveryMode;
    }

    private string? Resolve(string setting, string? configuredValue)
        => SmsSettings.Resolve(saved, setting, configuredValue);

    private static bool TryToE164(string phoneNumber, out string e164)
    {
        e164 = string.Empty;

        if (string.IsNullOrWhiteSpace(phoneNumber))
            return false;

        var trimmed = phoneNumber.Trim();
        if (!trimmed.StartsWith('+'))
        {
            // A bare national number has no country code and Twilio will not guess one; a default
            // dial code would send codes to the wrong country, so this is refused rather than
            // assumed. The application layer normalises before reaching here.
            return false;
        }

        var digits = trimmed[1..].Where(char.IsAsciiDigit).ToArray();
        if (digits.Length is < 7 or > 15)
            return false;

        e164 = "+" + new string(digits);
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
        "60203" => "Too many verification requests for this number; try again later.",
        "60212" => "The configured Verify Service SID is not valid.",
        "60243" => "Twilio rejects a custom message body; use an approved template instead.",
        "60600" => "The destination number is not reachable from the Twilio account's country.",
        _ => $"Twilio rejected the message ({code})."
    };

    private sealed record SendContext(
        string AccountSid,
        string AuthToken,
        string ServiceSid,
        string E164,
        string Otp,
        int ExpiryMinutes,
        SmsTemplateResolution Template);
}
