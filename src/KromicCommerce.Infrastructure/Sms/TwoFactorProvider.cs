using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
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
/// <b>Routes.</b> TwoFactor publishes more than one generation of this API. This adapter attempts,
/// in order:
/// </para>
/// <list type="number">
/// <item>
/// <b>Native OTP</b> — <see cref="TwoFactorOptions.OtpPath"/>, defaulting to the dynamic-template
/// OTP endpoint <c>POST /API/V1/OTP/SEND</c> authenticated with an <c>X-API-Key</c> header and a
/// <c>{ to, channel, template_name, var1 }</c> body. This is 2Factor's OTP-specific endpoint and
/// is preferred.
/// </item>
/// <item>
/// <b>Transactional template</b> — <see cref="TwoFactorOptions.TransactionalPath"/>, defaulting to
/// <c>POST /sms/{apiKey}/{template}</c>, which carries the template <b>name in the URL path</b>.
/// </item>
/// </list>
/// <para>
/// <b>The template name is read from the administrator's configuration on every request</b>, not
/// from a constant: it is the <c>externalTemplateId</c> of the active
/// <see cref="SmsTemplateSnapshot"/>, fetched per send by <see cref="SmsTemplateResolver"/>.
/// Registering a different template therefore takes effect on the next OTP with no restart.
/// </para>
/// <para>
/// <b>Field names are configuration.</b> 2Factor's own pages disagree on whether the template
/// field is <c>template_name</c>, <c>template</c> or <c>templateName</c>, and its machine-readable
/// reference could not be fetched to settle it. Every such name is therefore a setting, so an
/// account can be corrected without a rebuild — which is what makes this testable against a
/// sandbox whose reference differs.
/// </para>
/// <para>
/// <b>Fallback is deliberately narrow.</b> The transactional route is tried only when the native
/// route reports itself <i>unavailable</i> (404/405/501, or an explicit unsupported status) —
/// that is, when 2Factor has no dedicated OTP type for the account. It is deliberately not used
/// for timeouts, 5xx or rate limits: those are ambiguous about whether the first message was
/// delivered, and a second SMS in that situation costs money and can leave a customer holding one
/// of two codes. Those failures are reported as retryable instead.
/// </para>
/// </remarks>
internal sealed class TwoFactorProvider(
    IOptions<TwoFactorOptions> options,
    IOptions<SmsOtpPolicyOptions> otpPolicy,
    SmsProviderSettingsSnapshot? saved,
    ISmsTemplateStore templates,
    ISmsOtpAuditSink audit,
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
            logger.LogWarning(
                "2Factor rejected an OTP send: the destination is not a valid international number.");
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

        var expiryMinutes = otpPolicy.Value.ClampedExpiryMinutes;

        // Resolved per request, never cached: this is the administrator's template, and the whole
        // point is that changing it takes effect immediately.
        var template = await SmsTemplateResolver
            .ResolveAsync(templates, SmsProviderKind.TwoFactor, otp, expiryMinutes, cancellationToken)
            .ConfigureAwait(false);

        var mode = ResolveDeliveryMode(configured);
        var context = new SendContext(apiKey, destination, otp, expiryMinutes, template, mode);

        logger.LogDebug(
            "2Factor OTP send starting. Mode: {Mode}. Template: {TemplateName} (ref {TemplateReference}).",
            mode.ToName(),
            template.Exists ? template.AdminName : "(none)",
            template.Reference ?? "(none)");

        return mode switch
        {
            SmsOtpDeliveryMode.NativeOtp =>
                await SendNativeAsync(context, usedFallback: false, cancellationToken),

            SmsOtpDeliveryMode.TransactionalTemplate =>
                await SendTransactionalAsync(context, usedFallback: false, cancellationToken),

            _ => await SendAutoAsync(context, cancellationToken)
        };
    }

    /// <summary>
    /// Native first, transactional only when 2Factor reports the native route unsupported.
    /// </summary>
    private async Task<SmsSendResult> SendAutoAsync(SendContext context, CancellationToken cancellationToken)
    {
        var native = await SendNativeAsync(context, usedFallback: false, cancellationToken).ConfigureAwait(false);

        if (native.Success)
            return native;

        // Only an "unavailable route" answer is treated as an invitation to fall back. See the
        // type remarks for why an ambiguous failure must not produce a second SMS.
        if (!IsRouteUnavailable(native.ErrorCode))
        {
            if (native.Retryable)
                logger.LogWarning(
                    "2Factor native OTP route failed with {ErrorCode}; not falling back because the " +
                    "first attempt may already have been delivered.", native.ErrorCode);
            else
                logger.LogWarning(
                    "2Factor native OTP route was rejected with {ErrorCode}; the transactional " +
                    "fallback would fail the same way, so it was not attempted.", native.ErrorCode);

            return native;
        }

        logger.LogInformation(
            "2Factor has no usable native OTP route ({ErrorCode}); falling back to the transactional " +
            "template route using template '{TemplateName}'.", native.ErrorCode, context.Template.AdminName);

        return await SendTransactionalAsync(context, usedFallback: true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 2Factor's OTP-specific endpoint. Carries the administrator's template name and the OTP as
    /// the template's first variable.
    /// </summary>
    private async Task<SmsSendResult> SendNativeAsync(
        SendContext context, bool usedFallback, CancellationToken cancellationToken)
    {
        var configured = options.Value;

        var baseUrl = Resolve("BaseUrl", configured.BaseUrl) ?? configured.BaseUrl;
        // SendPath is honoured for administrators who saved it before the native/transactional
        // split, so upgrading does not silently point them at a different route.
        var otpPath = Resolve("OtpPath", null)
                      ?? Resolve("SendPath", configured.OtpPath)
                      ?? configured.OtpPath;
        var apiKeyHeader = Resolve("ApiKeyHeader", configured.ApiKeyHeader);
        var includeApiKeyInBody = string.IsNullOrWhiteSpace(apiKeyHeader);
        var templateField = Resolve("TemplateNameField", configured.TemplateNameField)
                           ?? configured.TemplateNameField;
        var channel = Resolve("Channel", configured.Channel) ?? configured.Channel;
        var otpVariable = Resolve("OtpVariableName", configured.OtpVariableName)
                          ?? configured.OtpVariableName;
        var expiryVariable = Resolve("ExpiryVariableName", configured.ExpiryVariableName)
                             ?? configured.ExpiryVariableName;
        var senderId = Resolve("SenderId", configured.SenderId);

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["to"] = context.Destination,
            ["channel"] = channel
        };

        // The template name comes from the user's configuration, so it is sent whenever one is
        // registered. An account whose OTP template needs no approval can omit it.
        if (context.Template.HasReference)
            payload[templateField] = context.Template.Reference;

        payload[otpVariable] = context.Otp;

        if (context.Template.Variables.Any(v => v.Key == SmsTemplateTokens.Expiry))
            payload[expiryVariable] = context.ExpiryMinutes.ToString(CultureInfo.InvariantCulture);

        if (!string.IsNullOrWhiteSpace(senderId))
            payload["from"] = senderId;

        // Some 2Factor account generations authenticate with `apiKey` in the body rather than the
        // X-API-Key header. Emptying ApiKeyHeader selects that shape, so the credential is not
        // placed in two places at once.
        if (includeApiKeyInBody)
            payload["apiKey"] = context.ApiKey;

        return await ExecuteAsync(
            context,
            route: "native",
            usedFallback: usedFallback,
            httpMethod: HttpMethod.Post,
            uri: SmsEndpoint.Build(baseUrl, otpPath),
            content: BuildJsonContent(payload),
            apiKey: context.ApiKey,
            apiKeyHeader: apiKeyHeader,
            includeApiKeyInBody: includeApiKeyInBody,
            cancellationToken);
    }

    /// <summary>
    /// 2Factor's transactional template route, where the template <b>name</b> is a URL segment.
    /// </summary>
    private async Task<SmsSendResult> SendTransactionalAsync(
        SendContext context, bool usedFallback, CancellationToken cancellationToken)
    {
        // Unlike the native endpoint, this route has no sensible form without a template: the
        // name is part of the URL. Failing here names the missing setting instead of producing a
        // gateway error about an unresolved route.
        if (!context.Template.HasReference)
        {
            logger.LogError(
                "2Factor is selected for a transactional send but no active template with a " +
                "registered template name exists.");
            return new SmsSendResult(false, null, "TEMPLATE_NOT_CONFIGURED",
                "2Factor requires an approved message template to be configured before it can send.", false);
        }

        var configured = options.Value;

        var baseUrl = Resolve("BaseUrl", configured.BaseUrl) ?? configured.BaseUrl;
        var templatePath = Resolve("TransactionalPath", configured.TransactionalPath)
                           ?? configured.TransactionalPath;
        var otpVariable = Resolve("OtpVariableName", configured.OtpVariableName)
                          ?? configured.OtpVariableName;
        var expiryVariable = Resolve("ExpiryVariableName", configured.ExpiryVariableName)
                             ?? configured.ExpiryVariableName;
        var senderId = Resolve("SenderId", configured.SenderId);

        // Placeholders are expanded from the user's configuration, and the values are URL-escaped
        // because template names legitimately contain spaces and the SDK example does exactly that.
        var path = templatePath
            .Replace("{apiKey}", Uri.EscapeDataString(context.ApiKey), StringComparison.Ordinal)
            .Replace("{template}", Uri.EscapeDataString(context.Template.Reference!), StringComparison.Ordinal);

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["To"] = context.Destination,
            ["Var1"] = context.Otp
        };

        if (context.Template.Variables.Any(v => v.Key == SmsTemplateTokens.Expiry))
            payload["Var2"] = context.ExpiryMinutes.ToString(CultureInfo.InvariantCulture);

        if (!string.IsNullOrWhiteSpace(senderId))
            payload["From"] = senderId;

        return await ExecuteAsync(
            context,
            route: "transactional",
            usedFallback: usedFallback,
            httpMethod: HttpMethod.Post,
            uri: SmsEndpoint.Build(baseUrl, path),
            content: BuildJsonContent(payload),
            apiKey: context.ApiKey,
            // The key is already in the path for this route; sending it again is harmless but
            // needlessly widens where the credential appears.
            apiKeyHeader: null,
            includeApiKeyInBody: false,
            cancellationToken);
    }

    /// <summary>
    /// Issues the HTTP call, interprets the response, and records exactly one audit entry for the
    /// attempt regardless of how it ended.
    /// </summary>
    private async Task<SmsSendResult> ExecuteAsync(
        SendContext context,
        string route,
        bool usedFallback,
        HttpMethod httpMethod,
        Uri uri,
        HttpContent content,
        string apiKey,
        string? apiKeyHeader,
        bool includeApiKeyInBody,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        void Publish(SmsSendResult result)
        {
            stopwatch.Stop();
            audit.Record(new SmsOtpAudit(
                Provider: ProviderName,
                Mode: context.Mode.ToName(),
                AttemptedRoute: route,
                UsedFallback: usedFallback,
                TemplateName: context.Template.Exists ? context.Template.AdminName : null,
                TemplateReference: context.Template.Reference,
                MaskedPhone: SmsPhoneNumber.Mask(context.Destination),
                Success: result.Success,
                ProviderMessageId: result.ProviderMessageId,
                ErrorCode: result.ErrorCode,
                Retryable: result.Retryable,
                DurationMs: stopwatch.ElapsedMilliseconds));
        }

        try
        {
            using var client = httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(httpMethod, uri) { Content = content };

            if (!string.IsNullOrWhiteSpace(apiKeyHeader))
                request.Headers.TryAddWithoutValidation(apiKeyHeader, apiKey);

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode && IsAccepted(body))
            {
                var sessionId = ReadString(body, "session_id") ?? ReadString(body, "SessionId")
                                ?? ReadString(body, "SessionID");

                logger.LogInformation(
                    "2Factor accepted the OTP via the {Route} route. Session: {SessionId}.", route, sessionId);

                var success = new SmsSendResult(true, sessionId, null, null, false);
                Publish(success);
                return success;
            }

            var code = ReadErrorCode(body, response.StatusCode);
            var retryable = IsRetryable(code, response.StatusCode);

            logger.LogWarning(
                "2Factor rejected the OTP via the {Route} route. Code: {Code}, HTTP {StatusCode}, retryable: {Retryable}.",
                route, code, (int)response.StatusCode, retryable);

            var failure = new SmsSendResult(false, null, code, DescribeError(code), retryable);
            Publish(failure);
            return failure;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "2Factor HTTP request failed on the {Route} route.", route);
            var result = new SmsSendResult(false, null, "HTTP_ERROR", "Could not reach 2Factor.", true);
            Publish(result);
            return result;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError("2Factor request timed out on the {Route} route.", route);
            var result = new SmsSendResult(false, null, "TIMEOUT", "Provider request timed out.", true);
            Publish(result);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected 2Factor error on the {Route} route.", route);
            var result = new SmsSendResult(false, null, "UNEXPECTED_ERROR", "Unexpected provider error.", false);
            Publish(result);
            return result;
        }
    }

    private static HttpContent BuildJsonContent(Dictionary<string, object?> payload)
        => System.Net.Http.Json.JsonContent.Create(payload, options: SmsJson.Options);

    /// <summary>
    /// 2Factor's published examples answer <c>{ "status": "sent" }</c>; its legacy routes answer
    /// <c>{ "Status": "Success" }</c> and have historically used HTTP 200 for application-level
    /// failures. A body with no status at all is therefore treated as accepted, but an explicit
    /// non-success status is not — reporting an undelivered code as sent is the one failure this
    /// must never make.
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

    /// <summary>
    /// True only when 2Factor says the route itself does not exist for this account. This is the
    /// sole condition that authorises the transactional fallback.
    /// </summary>
    private static bool IsRouteUnavailable(string? errorCode) => errorCode switch
    {
        "ROUTE_NOT_SUPPORTED" => true,
        "HTTP_404" or "HTTP_405" or "HTTP_501" => true,
        _ => false
    };

    private static bool IsRetryable(string code, HttpStatusCode status) => code switch
    {
        "RATE_LIMITED" => true,
        "GATEWAY_UNAVAILABLE" => true,
        _ => (int)status >= 500 || status == HttpStatusCode.RequestTimeout
             || status == HttpStatusCode.TooManyRequests
    };

    private SmsOtpDeliveryMode ResolveDeliveryMode(TwoFactorOptions configured)
    {
        var raw = SmsSettings.Resolve(saved, "DeliveryMode", configured.DeliveryMode.ToName());
        var parsed = SmsOtpDeliveryModes.Parse(raw);

        if (raw is not null && parsed is null)
            logger.LogWarning(
                "2Factor DeliveryMode '{Value}' is not recognised; using {Default}.",
                raw, SmsOtpDeliveryMode.Auto);

        return parsed ?? configured.DeliveryMode;
    }

    private string? Resolve(string setting, string? configuredValue)
        => SmsSettings.Resolve(saved, setting, configuredValue);

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

    /// <summary>
    /// The failure code to report and to decide fallback on.
    /// </summary>
    /// <remarks>
    /// A 2Factor error body on an HTTP 200 carries a generic status such as <c>Error</c>, which is
    /// useless for deciding anything. When the status code itself says the route does not exist,
    /// that is reported instead: it is both more informative to an operator and the signal that
    /// authorises the transactional fallback. An explicit vendor <c>code</c> field always wins,
    /// because it names the real reason.
    /// </remarks>
    private static string ReadErrorCode(string json, HttpStatusCode status)
    {
        var httpCode = $"HTTP_{(int)status}";

        string? vendorCode;
        try
        {
            using var document = JsonDocument.Parse(json);
            vendorCode = document.RootElement.TryGetProperty("code", out var code)
                         && code.ValueKind == JsonValueKind.String
                ? code.GetString()
                : null;
        }
        catch (JsonException)
        {
            vendorCode = null;
        }

        if (!string.IsNullOrWhiteSpace(vendorCode))
            return vendorCode;

        if (IsRouteUnavailable(httpCode))
            return httpCode;

        var statusText = ReadString(json, "status") ?? ReadString(json, "Status");

        return string.IsNullOrWhiteSpace(statusText) ? httpCode : statusText;
    }

    private static string DescribeError(string code) => code switch
    {
        "INVALID_API_KEY" or "UNAUTHORIZED" => "The 2Factor API key is not valid.",
        "INVALID_TEMPLATE" or "TEMPLATE_NOT_FOUND" =>
            "The configured 2Factor template is not approved for this account.",
        "TEMPLATE_MISMATCH" => "The message variables do not match the approved template.",
        "INVALID_NUMBER" or "INVALID_PHONE_NUMBER" => "The destination number is not valid.",
        "RATE_LIMITED" or "HTTP_429" => "Rate limit exceeded. Retry after the indicated delay.",
        "GATEWAY_UNAVAILABLE" => "The 2Factor gateway is temporarily unavailable.",
        "HTTP_404" or "HTTP_405" or "HTTP_501" or "ROUTE_NOT_SUPPORTED" =>
            "This 2Factor account does not expose that route.",
        _ => $"2Factor rejected the message ({code})."
    };

    /// <summary>Immutable per-send inputs shared by both routes.</summary>
    private sealed record SendContext(
        string ApiKey,
        string Destination,
        string Otp,
        int ExpiryMinutes,
        SmsTemplateResolution Template,
        SmsOtpDeliveryMode Mode);
}
