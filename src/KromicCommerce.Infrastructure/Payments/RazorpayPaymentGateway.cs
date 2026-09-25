using System.Security.Cryptography;
using System.Text;
using KromicCommerce.Application.Abstractions.Payments;
using KromicCommerce.Infrastructure.Configuration;
using Razorpay.Api;

namespace KromicCommerce.Infrastructure.Payments;

/// <summary>
/// Razorpay implementation of IPaymentGateway.
/// Razorpay SDK types are fully contained here — never exposed to Application or Domain.
/// API credentials are read from RazorpayOptions (environment variables).
/// No secrets are logged. Only provider-generated public identifiers are surfaced.
/// Amount conversion: Razorpay API expects amounts in smallest currency unit (paise for INR).
/// </summary>
internal sealed class RazorpayPaymentGateway(
    IOptions<RazorpayOptions> options,
    ILogger<RazorpayPaymentGateway> logger) : IPaymentGateway
{
    public string ProviderName => "Razorpay";

    public async Task<CreatePaymentOrderResult> CreateOrderAsync(
        Guid orderId,
        decimal amount,
        string currency,
        string receiptId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var opts = options.Value;
            var client = new RazorpayClient(opts.KeyId, opts.KeySecret);

            // Razorpay amounts are in smallest currency unit (paise for INR)
            var amountInPaise = (int)Math.Round(amount * 100);

            var orderOptions = new Dictionary<string, object>
            {
                ["amount"] = amountInPaise,
                ["currency"] = currency,
                ["receipt"] = receiptId,
                ["notes"] = new Dictionary<string, string>
                {
                    ["internal_order_id"] = orderId.ToString()
                }
            };

            var razorpayOrder = await Task.Run(
                () => client.Order.Create(orderOptions), cancellationToken);

            string providerOrderId = razorpayOrder["id"]?.ToString() ?? string.Empty;
            string logSafeId = providerOrderId; // avoid dynamic dispatch in logger
            logger.LogInformation(
                "Razorpay order created. InternalOrderId: {OrderId} ProviderOrderId: {ProviderOrderId}",
                orderId.ToString(), logSafeId);

            return new CreatePaymentOrderResult(true, providerOrderId, null);
        }
        catch (Exception ex)
        {
            // Do NOT log key/secret — only the message
            logger.LogError(ex,
                "Razorpay order creation failed for InternalOrderId: {OrderId}", orderId);
            return new CreatePaymentOrderResult(false, null, ex.Message);
        }
    }

    public bool VerifyPaymentSignature(string orderId, string paymentId, string signature)
    {
        try
        {
            var opts = options.Value;
            // Razorpay signature = HMAC-SHA256(orderId + "|" + paymentId, KeySecret)
            var payload = $"{orderId}|{paymentId}";
            var keyBytes = Encoding.UTF8.GetBytes(opts.KeySecret);
            var payloadBytes = Encoding.UTF8.GetBytes(payload);

            var computed = HMACSHA256.HashData(keyBytes, payloadBytes);
            var computedHex = Convert.ToHexString(computed).ToLowerInvariant();

            // Constant-time comparison to prevent timing attacks
            var valid = CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(computedHex),
                Encoding.UTF8.GetBytes(signature.ToLowerInvariant()));

            if (!valid)
                logger.LogWarning(
                    "Razorpay signature mismatch. OrderId: {OrderId} PaymentId: {PaymentId}",
                    orderId, paymentId);

            return valid;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during Razorpay signature verification");
            return false;
        }
    }

    public WebhookVerificationResult? VerifyWebhook(string rawPayload, string signature)
    {
        try
        {
            var opts = options.Value;
            // Razorpay webhook signature = HMAC-SHA256(rawPayload, WebhookSecret)
            var keyBytes = Encoding.UTF8.GetBytes(opts.WebhookSecret);
            var payloadBytes = Encoding.UTF8.GetBytes(rawPayload);
            var computed = Convert.ToHexString(HMACSHA256.HashData(keyBytes, payloadBytes))
                .ToLowerInvariant();

            var valid = CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(computed),
                Encoding.UTF8.GetBytes(signature.ToLowerInvariant()));

            if (!valid)
            {
                logger.LogWarning("Razorpay webhook signature invalid.");
                return null;
            }

            // Parse the minimal fields needed — no full SDK deserialization leaked to Application
            using var doc = System.Text.Json.JsonDocument.Parse(rawPayload);
            var root = doc.RootElement;

            var eventType = root.GetProperty("event").GetString() ?? string.Empty;
            string? paymentId = null, providerOrderId = null, eventId = null, failureReason = null;

            if (root.TryGetProperty("payload", out var payloadEl)
                && payloadEl.TryGetProperty("payment", out var paymentEl)
                && paymentEl.TryGetProperty("entity", out var entity))
            {
                paymentId = entity.TryGetProperty("id", out var pid) ? pid.GetString() : null;
                providerOrderId = entity.TryGetProperty("order_id", out var oid) ? oid.GetString() : null;
                failureReason = entity.TryGetProperty("error_description", out var err) ? err.GetString() : null;
            }
            eventId = root.TryGetProperty("account_id", out var aid) ? aid.GetString() : null;

            return new WebhookVerificationResult(
                EventType: eventType,
                ProviderPaymentId: paymentId,
                ProviderOrderId: providerOrderId,
                ProviderEventId: eventId,
                IsPaymentSucceeded: eventType == "payment.captured",
                IsPaymentFailed: eventType == "payment.failed",
                FailureReason: failureReason);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error parsing Razorpay webhook payload");
            return null;
        }
    }
}
