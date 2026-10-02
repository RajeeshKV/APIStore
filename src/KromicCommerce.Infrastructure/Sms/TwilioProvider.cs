using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using KromicCommerce.Application.Abstractions.Sms;
using KromicCommerce.Application.Options;
using KromicCommerce.Domain.Sms;
using KromicCommerce.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KromicCommerce.Infrastructure.Sms;

/// <summary>
/// Twilio Programmable Messaging adapter (Messages API).
/// </summary>
/// <remarks>
/// <para>
/// Sends the OTP code that this application generates through Twilio's Messages API:
/// <c>POST https://api.twilio.com/2010-04-01/Accounts/{AccountSid}/Messages.json</c>, authenticated
/// with HTTP Basic auth (AccountSid as username, AuthToken as password), body
/// <c>{ To, From, Body }</c>.
/// </para>
/// <para>
/// This adapter uses <b>Programmable Messaging</b>, not Twilio Verify. Twilio only delivers the SMS;
/// it does not generate, store, or verify the OTP. Expiry, attempt limits, and resend cooldown are
/// all owned by the application.
/// </para>
/// </remarks>
internal sealed class TwilioProvider(
    IOptions<TwilioOptions> options,
    SmsProviderSettingsSnapshot? saved,
    IHttpClientFactory httpClientFactory,
    ILogger<TwilioProvider> logger) : ISmsProvider
{
    public const string HttpClientName = "Twilio";

    public string ProviderName => "Twilio";

    public SmsProviderKind Kind => SmsProviderKind.Twilio;

    public async Task<SmsSendResult> SendOtpAsync(
        string phoneNumber, string otp, CancellationToken cancellationToken = default)
    {
        if (SmsPhoneNumber.TryToE164(phoneNumber) is not { } destination)
        {
            logger.LogWarning("Twilio rejected an OTP send: the destination is not a valid phone number.");
            return new SmsSendResult(false, null, "INVALID_PHONE_NUMBER",
                "The phone number is not a valid international number.", false);
        }

        var configured = options.Value;
        var accountSid = SmsSettings.Resolve(saved, SmsSettingNames.AccountSid, configured.AccountSid);
        var authToken = SmsSettings.Resolve(saved, SmsSettingNames.AuthToken, configured.AuthToken);
        var fromNumber = SmsSettings.Resolve(saved, SmsSettingNames.FromNumber, configured.FromNumber);

        if (string.IsNullOrWhiteSpace(accountSid) || string.IsNullOrWhiteSpace(authToken) || string.IsNullOrWhiteSpace(fromNumber))
        {
            logger.LogError("Twilio is selected but AccountSid, AuthToken, or FromNumber is missing.");
            return new SmsSendResult(false, null, "MISSING_CREDENTIALS",
                "Twilio credentials are not configured.", false);
        }

        var body = $"Your verification code is {otp}. It expires in {SmsOtpDefaults.ExpiryMinutes} minutes.";

        var payload = new SendRequest
        {
            To = destination,
            From = fromNumber,
            Body = body
        };

        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var client = httpClientFactory.CreateClient(HttpClientName);
            var uri = new Uri($"https://api.twilio.com/2010-04-01/Accounts/{Uri.EscapeDataString(accountSid)}/Messages.json");

            using var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["To"] = payload.To,
                    ["From"] = payload.From,
                    ["Body"] = payload.Body
                })
            };

            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{accountSid}:{authToken}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();

            if (response.IsSuccessStatusCode)
            {
                var sid = ReadString(responseBody, "sid");

                logger.LogInformation(
                    "Twilio accepted the OTP. Message SID: {Sid}. DurationMs: {DurationMs}",
                    sid, stopwatch.ElapsedMilliseconds);

                return new SmsSendResult(true, sid, null, null, false);
            }

            var twilioCode = ReadInt(responseBody, "code")?.ToString(System.Globalization.CultureInfo.InvariantCulture)
                             ?? $"HTTP_{(int)response.StatusCode}";
            var retryable = (int)response.StatusCode >= 500;

            logger.LogWarning(
                "Twilio rejected the OTP. Code: {Code}, HTTP {StatusCode}, retryable: {Retryable}. DurationMs: {DurationMs}",
                twilioCode, (int)response.StatusCode, retryable, stopwatch.ElapsedMilliseconds);

            return new SmsSendResult(false, null, twilioCode, DescribeError(twilioCode), retryable);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Twilio HTTP request failed. DurationMs: {DurationMs}", stopwatch.ElapsedMilliseconds);
            return new SmsSendResult(false, null, "HTTP_ERROR", "Could not reach Twilio.", true);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError("Twilio request timed out. DurationMs: {DurationMs}", stopwatch.ElapsedMilliseconds);
            return new SmsSendResult(false, null, "TIMEOUT", "Provider request timed out.", true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected Twilio error.");
            return new SmsSendResult(false, null, "UNEXPECTED_ERROR", "Unexpected provider error.", false);
        }
    }

    private static string DescribeError(string code) => code switch
    {
        "20001" => "The request to Twilio could not be completed.",
        "20003" => "The Twilio auth token is not valid.",
        "20005" => "The requested resource does not exist.",
        "21211" => "The destination number is not valid.",
        "21212" => "The From number is not valid.",
        "21214" => "The destination number is not valid for SMS.",
        "21408" => "The destination number is not enabled for SMS.",
        "60200" => "Could not route the message to any carrier.",
        "60202" => "The destination number cannot receive SMS.",
        "60600" => "The destination number is not reachable from this account.",
        _ => $"Twilio rejected the message ({code})."
    };

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

    private sealed class SendRequest
    {
        [JsonPropertyName("To")]
        public string To { get; init; } = string.Empty;

        [JsonPropertyName("From")]
        public string From { get; init; } = string.Empty;

        [JsonPropertyName("Body")]
        public string Body { get; init; } = string.Empty;
    }
}
