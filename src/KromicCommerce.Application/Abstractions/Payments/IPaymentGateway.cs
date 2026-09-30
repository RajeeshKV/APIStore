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

    /// <summary>Whether the gateway has an enabled, usable configuration.</summary>
    Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default);

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
    Task<bool> VerifyPaymentSignatureAsync(
        string orderId,
        string paymentId,
        string signature,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies and extracts structured data from a raw webhook payload.
    /// Returns null when signature validation fails.
    /// </summary>
    Task<WebhookVerificationResult?> VerifyWebhookAsync(
        string rawPayload,
        string signature,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Initiates a full refund for a previously captured payment.
    /// Called by the admin cancel flow when the order has a paid Razorpay payment.
    /// Speed is "normal" by default — refunds appear in 5-7 business days.
    /// </summary>
    Task<RefundResult> RefundAsync(
        string providerPaymentId,
        decimal amount,
        string notes,
        CancellationToken cancellationToken = default);
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

public sealed record RefundResult(
    bool Success,
    string? ProviderRefundId,
    string? ErrorMessage);
