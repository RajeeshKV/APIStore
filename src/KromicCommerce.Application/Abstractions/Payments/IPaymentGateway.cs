namespace KromicCommerce.Application.Abstractions.Payments;

/// <summary>
/// Provider-independent payment gateway abstraction.
/// No Razorpay SDK types cross this boundary — they stay in Infrastructure.
/// The Application layer only sees these result types.
/// Amount must always come from the server-calculated order total — never from the frontend.
/// </summary>
public interface IPaymentGateway
{
    string ProviderName { get; }

    /// <summary>Creates a payment order at the provider (e.g. Razorpay order).</summary>
    Task<CreatePaymentOrderResult> CreateOrderAsync(
        Guid orderId,
        decimal amount,
        string currency,
        string receiptId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies the payment signature returned by the frontend after checkout.
    /// Must be called before marking an order as paid.
    /// Returns false for any invalid signature — never trust the frontend directly.
    /// </summary>
    bool VerifyPaymentSignature(
        string orderId,
        string paymentId,
        string signature);

    /// <summary>
    /// Verifies and extracts structured data from a raw webhook payload.
    /// Returns null when signature validation fails.
    /// </summary>
    WebhookVerificationResult? VerifyWebhook(
        string rawPayload,
        string signature);
}

public sealed record CreatePaymentOrderResult(
    bool Success,
    string? ProviderOrderId,
    string? ErrorMessage);

public sealed record WebhookVerificationResult(
    string EventType,
    string? ProviderPaymentId,
    string? ProviderOrderId,
    string? ProviderEventId,
    bool IsPaymentSucceeded,
    bool IsPaymentFailed,
    string? FailureReason);
