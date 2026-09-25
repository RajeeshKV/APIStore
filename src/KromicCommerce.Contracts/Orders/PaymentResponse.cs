namespace KromicCommerce.Contracts.Orders;

/// <summary>
/// Payment status returned to the customer or admin.
/// Never exposes KeySecret, WebhookSecret, or any provider credential.
/// </summary>
public sealed record PaymentResponse(
    Guid Id,
    Guid OrderId,
    string Provider,

    /// <summary>Provider-generated payment ID (e.g. Razorpay pay_xxx). Not a secret.</summary>
    string? ProviderPaymentId,

    decimal Amount,
    string Currency,
    string Status,
    DateTime? PaidAt,
    DateTime CreatedAtUtc);
