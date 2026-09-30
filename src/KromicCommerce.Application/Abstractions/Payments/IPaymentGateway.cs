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
    ///
    /// Called by the cancellation flow when the order has a captured Razorpay payment.
    /// Speed is "normal" by default — refunds appear in 5-7 business days, so a successful
    /// result means the provider ACCEPTED the refund, not that funds have settled.
    ///
    /// <paramref name="idempotencyKey"/> must be stable for a given logical refund so that a
    /// retry of the same business operation is de-duplicated by the provider instead of
    /// issuing a second refund. Callers should derive it from the order ID.
    ///
    /// A failure must be reported through <see cref="RefundResult.Success"/>; the caller is
    /// responsible for refusing to cancel the order in that case.
    /// </summary>
    Task<RefundResult> RefundAsync(
        string providerPaymentId,
        decimal amount,
        string notes,
        string? idempotencyKey = null,
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
