using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using KromicCommerce.Application.Abstractions.Sms;
using KromicCommerce.Infrastructure.Configuration;

namespace KromicCommerce.Infrastructure.Sms;

/// <summary>
/// Free2SMS adapter (REST API v1).
///
/// Reference: https://free2sms.com/api/v1/send
/// </summary>
/// <remarks>
/// Free2SMS returns a machine-readable code at <c>details.code</c> for documented errors but at
/// the top level for rate limiting and infrastructure failures, so both are read. A rejected
/// send is never billed, which makes retrying after fixing a 4xx safe.
/// </remarks>
internal sealed class Free2SmsProvider(
    IOptions<Free2SmsOptions> options,
    IOptions<SmsOtpPolicyOptions> otpPolicy,
    SmsProviderSettingsSnapshot? saved,
    ISmsTemplateStore templates,
    ISmsOtpAuditSink audit,
    IHttpClientFactory httpClientFactory,
    ILogger<Free2SmsProvider> logger) : ISmsProvider
{
    public const string HttpClientName = "Free2Sms";

    /// <summary>
    /// Used when no template has been registered in the admin surface. Free2SMS matches the body
    /// against approved DLT templates itself, so this does not have to be an exact copy of a
    /// registration — but a store operating under DLT rules should register this text and add a
    /// matching <see cref="SmsTemplateSnapshot"/>.
    /// </summary>
    private const string FallbackOtpBody =
        "Your verification code is {OTP}. It expires in {EXPIRY_MINUTES} minutes.";

    public string ProviderName => "Free2SMS";

    public SmsProviderKind Kind => SmsProviderKind.Free2Sms;

    public async Task<SmsSendResult> SendOtpAsync(
        string phoneNumber, string otp, CancellationToken cancellationToken = default)
    {
        if (SmsPhoneNumber.TryToNational(phoneNumber) is not { } national)
        {
            return new SmsSendResult(false, null, "INVALID_PHONE_NUMBER",
                "The phone number is not a valid 10-digit mobile number.", false);
        }

        var configured = options.Value;
        var apiKey = SmsSettings.Resolve(saved, "ApiKey", configured.ApiKey);
        var senderId = SmsSettings.Resolve(saved, "SenderId", configured.SenderId);

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(senderId))
        {
            logger.LogError("Free2SMS is selected but ApiKey or SenderId is missing.");
            return new SmsSendResult(false, null, "MISSING_CREDENTIALS",
                "Free2SMS credentials are not configured.", false);
        }

        var baseUrl = SmsSettings.Resolve(saved, "BaseUrl", configured.BaseUrl) ?? configured.BaseUrl;
        var route = SmsSettings.Resolve(saved, "Route", configured.Route) ?? "otp";

        var expiryMinutes = otpPolicy.Value.ClampedExpiryMinutes;

        // Resolved from the administrator's configuration on every request, so switching the DLT
        // template takes effect on the next OTP without a restart.
        var template = await SmsTemplateResolver
            .ResolveAsync(templates, SmsProviderKind.Free2Sms, otp, expiryMinutes, cancellationToken)
            .ConfigureAwait(false);

        var message = template.HasBody
            ? template.Render()
            : FallbackOtpBody
                .Replace("{OTP}", otp, StringComparison.Ordinal)
                .Replace("{EXPIRY_MINUTES}", expiryMinutes.ToString(
                    CultureInfo.InvariantCulture), StringComparison.Ordinal);

        // DLT IDs are stored as text because a registration can exceed Int64 in some schemes.
        // A value we cannot fit in a long is dropped rather than sent malformed: Free2SMS then
        // falls back to matching on content, which is slower to reject but never bills a bad
        // request. Note the message text must still match the approved template exactly.
        var templateId = ParseTemplateId(template.Reference);
        if (template.HasReference && templateId is null)
        {
            logger.LogWarning(
                "Free2SMS template '{Name}' has a template ID that is not a 64-bit integer; " +
                "sending without it and relying on content matching.", template.AdminName);
        }

        var stopwatch = Stopwatch.StartNew();

        var payload = new SendRequest
        {
            Numbers = national,
            Message = message,
            SenderId = senderId,
            Route = route,
            TemplateId = templateId
        };

        try
        {
            using var client = httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, SmsEndpoint.Build(baseUrl, "send"))
            {
                Content = JsonContent.Create(payload, options: SmsJson.Options)
            };
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");

            using var response = await client.SendAsync(request, cancellationToken);
            var body = await ReadBodyAsync(response, cancellationToken);

            if (response.IsSuccessStatusCode && body?.Success == true)
            {
                var messageId = body.Reference ?? body.SmsLogId?.ToString(CultureInfo.InvariantCulture);
                logger.LogInformation("Free2SMS accepted OTP. Reference: {Reference}", body.Reference);

                var success = new SmsSendResult(true, messageId, null, null, false);
                Publish(national, template, success, stopwatch);
                return success;
            }

            var code = body?.Details?.Code ?? body?.Code ?? $"HTTP_{(int)response.StatusCode}";
            var retryable = IsRetryable(code, response.StatusCode);

            logger.LogWarning("Free2SMS rejected the OTP. Code: {Code}, HTTP {StatusCode}", code, (int)response.StatusCode);
            var failure = new SmsSendResult(false, null, code, DescribeError(code), retryable);
            Publish(national, template, failure, stopwatch);
            return failure;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Free2SMS HTTP request failed");
            var result = new SmsSendResult(false, null, "HTTP_ERROR", "Could not reach Free2SMS.", true);
            Publish(national, template, result, stopwatch);
            return result;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError("Free2SMS request timed out");
            var result = new SmsSendResult(false, null, "TIMEOUT", "Provider request timed out.", true);
            Publish(national, template, result, stopwatch);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected Free2SMS error");
            var result = new SmsSendResult(false, null, "UNEXPECTED_ERROR", "Unexpected provider error.", false);
            Publish(national, template, result, stopwatch);
            return result;
        }
    }

    /// <summary>
    /// Free2SMS exposes no OTP-specific endpoint, so the transactional DLT template is always the
    /// route taken. It is recorded as such so the audit trail shows why no native attempt was made.
    /// </summary>
    private void Publish(
        string national, SmsTemplateResolution template, SmsSendResult result, Stopwatch stopwatch)
    {
        stopwatch.Stop();
        audit.Record(new SmsOtpAudit(
            Provider: ProviderName,
            Mode: SmsOtpDeliveryMode.TransactionalTemplate.ToName(),
            AttemptedRoute: "transactional",
            UsedFallback: false,
            TemplateName: template.Exists ? template.AdminName : null,
            TemplateReference: template.Reference,
            MaskedPhone: SmsPhoneNumber.Mask(national),
            Success: result.Success,
            ProviderMessageId: result.ProviderMessageId,
            ErrorCode: result.ErrorCode,
            Retryable: result.Retryable,
            DurationMs: stopwatch.ElapsedMilliseconds));
    }

    private static long? ParseTemplateId(string? value)
        => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static async Task<SendResponse?> ReadBodyAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<SendResponse>(cancellationToken: cancellationToken);
        }
        catch (Exception)
        {
            // A non-JSON body (gateway HTML error page, empty 5xx) is expected on failure
            // paths and must not mask the HTTP status we already have.
            return null;
        }
    }

    /// <summary>
    /// Configuration and template problems must not be retried; gateway and throttling
    /// failures must be, and none of them charge the wallet.
    /// </summary>
    private static bool IsRetryable(string code, HttpStatusCode status) => code switch
    {
        "TEMPLATE_LOOKUP_FAILED" => true,
        "SEND_FAILED" => true,
        "GATEWAY_UNAVAILABLE" => true,
        "RATE_LIMITED" => true,
        _ => (int)status >= 500 || status == HttpStatusCode.TooManyRequests || status == HttpStatusCode.RequestTimeout
    };

    private static string DescribeError(string code) => code switch
    {
        "NO_API_KEY" => "No API key was sent.",
        "INVALID_API_KEY" => "The API key is not valid.",
        "ACCOUNT_INACTIVE" => "The Free2SMS account is not active.",
        "INVALID_SENDER" or "SENDER_MISSING" => "The sender ID is not approved for this account.",
        "INVALID_ROUTE" => "The delivery route is not valid.",
        "NO_VALID_NUMBERS" => "No valid mobile number was parsed from the request.",
        "TEMPLATE_MISMATCH" => "The message does not match an approved DLT template.",
        "TEMPLATE_AMBIGUOUS" => "More than one approved template matched; set an explicit template ID.",
        "TEMPLATE_NOT_FOUND" => "The configured template ID is not approved for this account.",
        "SENDER_TEMPLATE_MISMATCH" => "That template is not registered against the configured sender ID.",
        "INSUFFICIENT_BALANCE" => "The Free2SMS wallet balance is too low.",
        "NO_TEMPLATES" => "The account has no approved DLT templates.",
        "RATE_LIMITED" => "Rate limit exceeded. Retry after the indicated delay.",
        _ => $"Free2SMS rejected the message ({code})."
    };

    private sealed class SendRequest
    {
        [JsonPropertyName("numbers")]
        public string Numbers { get; init; } = string.Empty;

        [JsonPropertyName("message")]
        public string Message { get; init; } = string.Empty;

        [JsonPropertyName("sender_id")]
        public string SenderId { get; init; } = string.Empty;

        [JsonPropertyName("route")]
        public string Route { get; init; } = "otp";

        [JsonPropertyName("template_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public long? TemplateId { get; init; }
    }

    private sealed class SendResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("reference")]
        public string? Reference { get; init; }

        [JsonPropertyName("sms_log_id")]
        public long? SmsLogId { get; init; }

        [JsonPropertyName("code")]
        public string? Code { get; init; }

        [JsonPropertyName("details")]
        public ErrorDetails? Details { get; init; }
    }

    private sealed class ErrorDetails
    {
        [JsonPropertyName("code")]
        public string? Code { get; init; }
    }
}
