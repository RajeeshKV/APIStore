using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using KromicCommerce.Application.Abstractions.Sms;
using KromicCommerce.Domain.Sms;
using KromicCommerce.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KromicCommerce.Infrastructure.Sms;

/// <summary>
/// Free2SMS adapter (REST API v1).
/// </summary>
/// <remarks>
/// <para>
/// Sends the OTP code that this application generates through Free2SMS's standard SMS endpoint:
/// <c>POST https://free2sms.com/api/v1/send</c>, authenticated with a bearer token, body
/// <c>{ numbers, message, sender_id, route }</c>. The message template is rendered with the
/// generated OTP by replacing <c>{{OTP}}</c>.
/// </para>
/// <para>
/// Free2SMS does not generate, store, or verify the OTP. The message text must match an approved
/// DLT template exactly, which the administrator registers in the Free2SMS dashboard.
/// </para>
/// </remarks>
internal sealed class Free2SmsProvider(
    IOptions<Free2SmsOptions> options,
    SmsProviderSettingsSnapshot? saved,
    IHttpClientFactory httpClientFactory,
    ILogger<Free2SmsProvider> logger) : ISmsProvider
{
    public const string HttpClientName = "Free2Sms";

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
        var apiKey = SmsSettings.Resolve(saved, SmsSettingNames.ApiKey, configured.ApiKey);
        var senderId = SmsSettings.Resolve(saved, SmsSettingNames.SenderId, configured.SenderId);
        var messageTemplate = SmsSettings.Resolve(saved, SmsSettingNames.MessageTemplate, configured.MessageTemplate);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogError("Free2SMS is selected but ApiKey is missing.");
            return new SmsSendResult(false, null, "MISSING_CREDENTIALS",
                "Free2SMS credentials are not configured.", false);
        }

        if (string.IsNullOrWhiteSpace(senderId))
        {
            logger.LogError("Free2SMS is selected but SenderId is missing.");
            return new SmsSendResult(false, null, "MISSING_CREDENTIALS",
                "Free2SMS sender ID is not configured.", false);
        }

        if (string.IsNullOrWhiteSpace(messageTemplate))
        {
            logger.LogError("Free2SMS is selected but MessageTemplate is missing.");
            return new SmsSendResult(false, null, "MISSING_TEMPLATE",
                "An OTP message template must be configured before sending.", false);
        }

        var message = messageTemplate.Replace("{{OTP}}", otp, StringComparison.Ordinal);

        var payload = new SendRequest
        {
            Numbers = national,
            Message = message,
            SenderId = senderId,
            Route = "otp"
        };

        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var client = httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                new Uri("https://free2sms.com/api/v1/send"))
            {
                Content = JsonContent.Create(payload, options: SmsJson.Options)
            };

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await ReadBodyAsync(response, cancellationToken);

            stopwatch.Stop();

            if (response.IsSuccessStatusCode && body?.Success == true)
            {
                var messageId = body.Reference ?? body.SmsLogId?.ToString(System.Globalization.CultureInfo.InvariantCulture);

                logger.LogInformation(
                    "Free2SMS accepted the OTP. Reference: {Reference}. DurationMs: {DurationMs}",
                    body.Reference, stopwatch.ElapsedMilliseconds);

                return new SmsSendResult(true, messageId, null, null, false);
            }

            var code = body?.Details?.Code ?? body?.Code ?? $"HTTP_{(int)response.StatusCode}";
            var retryable = IsRetryable(code, response.StatusCode);

            logger.LogWarning(
                "Free2SMS rejected the OTP. Code: {Code}, HTTP {StatusCode}, retryable: {Retryable}. DurationMs: {DurationMs}",
                code, (int)response.StatusCode, retryable, stopwatch.ElapsedMilliseconds);

            return new SmsSendResult(false, null, code, DescribeError(code), retryable);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Free2SMS HTTP request failed. DurationMs: {DurationMs}", stopwatch.ElapsedMilliseconds);
            return new SmsSendResult(false, null, "HTTP_ERROR", "Could not reach Free2SMS.", true);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError("Free2SMS request timed out. DurationMs: {DurationMs}", stopwatch.ElapsedMilliseconds);
            return new SmsSendResult(false, null, "TIMEOUT", "Provider request timed out.", true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected Free2SMS error.");
            return new SmsSendResult(false, null, "UNEXPECTED_ERROR", "Unexpected provider error.", false);
        }
    }

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
        "TEMPLATE_AMBIGUOUS" => "More than one approved template matched.",
        "TEMPLATE_NOT_FOUND" => "The template ID is not approved for this account.",
        "SENDER_TEMPLATE_MISMATCH" => "That template is not registered against the configured sender ID.",
        "INSUFFICIENT_BALANCE" => "The Free2SMS wallet balance is too low.",
        "NO_TEMPLATES" => "The account has no approved DLT templates.",
        "RATE_LIMITED" => "Rate limit exceeded. Retry after the indicated delay.",
        _ => $"Free2SMS rejected the message ({code})."
    };

    private static async Task<SendResponse?> ReadBodyAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<SendResponse>(cancellationToken: cancellationToken);
        }
        catch (Exception)
        {
            return null;
        }
    }

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
