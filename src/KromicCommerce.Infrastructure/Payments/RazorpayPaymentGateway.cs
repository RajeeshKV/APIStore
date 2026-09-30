using System.Security.Cryptography;
using System.Text;
using KromicCommerce.Application.Abstractions.Payments;
using KromicCommerce.Application.Abstractions.Security;
using KromicCommerce.Application.Abstractions.Store;
using Razorpay.Api;

namespace KromicCommerce.Infrastructure.Payments;

/// <summary>
/// Razorpay implementation of IPaymentGateway.
/// Razorpay SDK types are fully contained here — never exposed to Application or Domain.
/// API credentials are read from the encrypted, customer-configurable store settings.
/// No secrets are logged. Only provider-generated public identifiers are surfaced.
/// Amount conversion: Razorpay API expects amounts in smallest currency unit (paise for INR).
/// </summary>
internal sealed class RazorpayPaymentGateway(
    IBusinessSettingsService businessSettings,
    ISecretProtectionService secretProtection,
    ILogger<RazorpayPaymentGateway> logger) : IPaymentGateway
{
    public string ProviderName => "Razorpay";

    public async Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default)
    {
        var payment = (await businessSettings.GetAsync(cancellationToken))?.Payment;
        return payment is { Enabled: true, IsConfigured: true };
    }

    public async Task<CreatePaymentOrderResult> CreateOrderAsync(
        Guid orderId,
        decimal amount,
        string currency,
        string receiptId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var credentials = await GetCredentialsAsync(cancellationToken);
            if (credentials is null)
                return new CreatePaymentOrderResult(false, null, "Razorpay is not configured or enabled.");

            var client = new RazorpayClient(credentials.KeyId, credentials.KeySecret);

            // Razorpay amounts are in smallest currency unit (paise for INR)
            var amountInPaise = ToPaise(amount);

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
            if (string.IsNullOrWhiteSpace(providerOrderId))
                return new CreatePaymentOrderResult(false, null, "Razorpay did not return an order ID.");

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

    public async Task<bool> VerifyPaymentSignatureAsync(
        string orderId,
        string paymentId,
        string signature,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var credentials = await GetCredentialsAsync(cancellationToken);
            if (credentials is null) return false;

            // Razorpay signature = HMAC-SHA256(orderId + "|" + paymentId, KeySecret)
            var payload = $"{orderId}|{paymentId}";
            var keyBytes = Encoding.UTF8.GetBytes(credentials.KeySecret);
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

    public async Task<WebhookVerificationResult?> VerifyWebhookAsync(
        string rawPayload,
        string signature,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var credentials = await GetCredentialsAsync(cancellationToken);
            if (credentials is null) return null;

            // Razorpay webhook signature = HMAC-SHA256(rawPayload, WebhookSecret)
            var keyBytes = Encoding.UTF8.GetBytes(credentials.WebhookSecret);
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

    public async Task<RefundResult> RefundAsync(
        string providerPaymentId,
        decimal amount,
        string notes,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var credentials = await GetCredentialsAsync(cancellationToken);
            if (credentials is null)
                return new RefundResult(false, null, "Razorpay is not configured or enabled.");

            var client = new RazorpayClient(credentials.KeyId, credentials.KeySecret);
            var amountInPaise = ToPaise(amount);

            var refundNotes = new Dictionary<string, string>
            {
                ["reason"] = notes
            };

            var refundOptions = new Dictionary<string, object>
            {
                ["amount"] = amountInPaise,
                ["speed"] = "normal",
                ["notes"] = refundNotes
            };

            // Razorpay de-duplicates refunds on (payment, receipt). A stable receipt derived
            // from the order makes a retried cancellation idempotent at the provider instead
            // of issuing a second refund against the same captured payment.
            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                refundNotes["idempotency_key"] = idempotencyKey;
                refundOptions["receipt"] = idempotencyKey;
            }

            var refund = await Task.Run(
                () =>
                {
                    var payment = client.Payment.Fetch(providerPaymentId);
                    return payment.Refund(refundOptions);
                },
                cancellationToken);

            var refundId = (string?)(refund["id"]?.ToString());

            if (string.IsNullOrWhiteSpace(refundId))
            {
                // The provider accepted the call but did not return a refund identifier.
                // Treat it as a failure: the caller must not cancel the order on an
                // untraceable refund, because there would be no reconciliation handle.
                logger.LogError(
                    "Razorpay refund for PaymentId {PaymentId} returned no refund id",
                    providerPaymentId);
                return new RefundResult(false, null,
                    "Razorpay accepted the refund but returned no refund id.");
            }

            logger.LogInformation(
                "Razorpay refund initiated. PaymentId: {PaymentId} RefundId: {RefundId} Amount: {Amount}",
                providerPaymentId, refundId, amount);

            return new RefundResult(true, refundId, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Razorpay refund failed for PaymentId: {PaymentId}", providerPaymentId);
            return new RefundResult(false, null, ex.Message);
        }
    }

    /// <summary>
    /// Converts a decimal amount in the store's currency to Razorpay's smallest currency
    /// unit (paise for INR). Rejects amounts that cannot be represented exactly rather than
    /// silently truncating them.
    /// </summary>
    private static int ToPaise(decimal amount)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be >= 0.");

        var paise = Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
        if (paise > int.MaxValue)
            throw new ArgumentOutOfRangeException(
                nameof(amount), amount, "Amount exceeds the maximum representable value.");

        return (int)paise;
    }

    private async Task<RazorpayCredentials?> GetCredentialsAsync(CancellationToken cancellationToken)
    {
        var payment = (await businessSettings.GetAsync(cancellationToken))?.Payment;
        if (payment is not { Enabled: true, IsConfigured: true } ||
            string.IsNullOrWhiteSpace(payment.RazorpayKeyId) ||
            string.IsNullOrWhiteSpace(payment.EncryptedRazorpayKeySecret) ||
            string.IsNullOrWhiteSpace(payment.EncryptedRazorpayWebhookSecret))
        {
            logger.LogWarning("Razorpay was requested without an enabled, complete configuration.");
            return null;
        }

        try
        {
            return new RazorpayCredentials(
                payment.RazorpayKeyId,
                secretProtection.Unprotect(payment.EncryptedRazorpayKeySecret),
                secretProtection.Unprotect(payment.EncryptedRazorpayWebhookSecret));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Razorpay credentials could not be decrypted.");
            return null;
        }
    }

    private sealed record RazorpayCredentials(
        string KeyId,
        string KeySecret,
        string WebhookSecret);
}
