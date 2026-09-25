using System.Net.Http.Json;
using KromicCommerce.Application.Abstractions.Sms;
using KromicCommerce.Infrastructure.Configuration;

namespace KromicCommerce.Infrastructure.Sms;

/// <summary>
/// Fast2SMS SMS provider adapter.
/// API documentation: https://docs.fast2sms.com
/// DLT registration is required for sending OTPs in India.
/// Credentials are read from SmsOptions.ProviderSettings["ApiKey"].
/// Never log the OTP or API key.
/// </summary>
internal sealed class Fast2SmsProvider(
    IOptions<SmsOptions> options,
    IHttpClientFactory httpClientFactory,
    ILogger<Fast2SmsProvider> logger) : ISmsProvider
{
    public string ProviderName => "Fast2SMS";

    private const string BaseUrl = "https://www.fast2sms.com/dev/bulkV2";

    public async Task<SmsSendResult> SendOtpAsync(
        string phoneNumber,
        string otp,
        CancellationToken cancellationToken = default)
    {
        if (!options.Value.ProviderSettings.TryGetValue("ApiKey", out var apiKey)
            || string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogError("Fast2SMS ApiKey is not configured in Sms:ProviderSettings:ApiKey");
            return new SmsSendResult(false, null, "MISSING_API_KEY", "API key not configured.", false);
        }

        try
        {
            using var client = httpClientFactory.CreateClient("Fast2SMS");

            // Fast2SMS Quick SMS (OTP) endpoint
            var queryString = $"?authorization={Uri.EscapeDataString(apiKey)}" +
                              $"&variables_values={Uri.EscapeDataString(otp)}" +
                              $"&route=otp" +
                              $"&numbers={Uri.EscapeDataString(phoneNumber.TrimStart('+'))}";

            var response = await client.GetAsync(BaseUrl + queryString, cancellationToken);
            var body = await response.Content.ReadFromJsonAsync<Fast2SmsResponse>(cancellationToken: cancellationToken);

            if (body?.Return == true)
            {
                var messageId = body.RequestId ?? string.Empty;
                logger.LogInformation("Fast2SMS OTP sent. RequestId: {RequestId}", messageId);
                return new SmsSendResult(true, messageId, null, null, false);
            }

            var errorMsg = body?.Message?.FirstOrDefault() ?? "Unknown error";
            logger.LogWarning("Fast2SMS returned failure. Message: {Message}", errorMsg);
            return new SmsSendResult(false, null, "PROVIDER_ERROR", errorMsg, true);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Fast2SMS HTTP request failed");
            return new SmsSendResult(false, null, "HTTP_ERROR", ex.Message, true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected Fast2SMS error");
            return new SmsSendResult(false, null, "UNEXPECTED_ERROR", ex.Message, false);
        }
    }

    // Minimal deserialization of Fast2SMS response — only what we need
    private sealed record Fast2SmsResponse(
        bool Return,
        string[]? Message,
        string? RequestId);
}
