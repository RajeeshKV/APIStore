using KromicCommerce.Domain.Orders.Events;

namespace KromicCommerce.Domain.Orders;

/// <summary>
/// Payment record for an order.
/// Stores provider identifiers and status — never stores secrets, card details, or CVV.
/// Amount comes from the server-calculated order grand total — never from the frontend.
/// </summary>
public sealed class Payment : AuditableEntity
{
    private Payment() { } // EF constructor

    public static Payment Create(
        Guid orderId,
        string provider,
        decimal amount,
        string currencyCode)
    {
        if (string.IsNullOrWhiteSpace(provider))
            throw new ArgumentException("Provider is required.", nameof(provider));
        if (amount < 0)
            throw new ArgumentException("Payment amount must be >= 0.", nameof(amount));

        var payment = new Payment
        {
            OrderId = orderId,
            Provider = provider,
            Amount = amount,
            CurrencyCode = currencyCode,
            Status = PaymentStatus.Pending
        };
        payment.RaiseDomainEvent(new PaymentCreatedEvent(payment.Id, orderId, amount, currencyCode));
        return payment;
    }

    public Guid OrderId { get; private set; }

    /// <summary>Provider name: "Razorpay", "CashOnDelivery", etc.</summary>
    public string Provider { get; private set; } = string.Empty;

    /// <summary>Provider-generated payment ID (e.g. Razorpay pay_xxx). Never a secret.</summary>
    public string? ProviderPaymentId { get; private set; }

    /// <summary>Provider-generated order ID (e.g. Razorpay order_xxx). Never a secret.</summary>
    public string? ProviderOrderId { get; private set; }

    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; } = "INR";
    public PaymentStatus Status { get; private set; }

    public string? FailureReason { get; private set; }
    public DateTime? PaidAt { get; private set; }

    // -----------------------------------------------------------------------
    // Refund tracking
    //
    // Persisting the refund result is the idempotency checkpoint for cancellation:
    // once a payment is marked refunded, a retried cancellation reads this state and
    // skips the provider call instead of issuing a second refund.
    // -----------------------------------------------------------------------

    /// <summary>Provider-generated refund ID (e.g. Razorpay rfnd_xxx). Never a secret.</summary>
    public string? ProviderRefundId { get; private set; }

    /// <summary>Amount accepted for refund by the provider.</summary>
    public decimal RefundedAmount { get; private set; }

    /// <summary>When the refund was accepted by the provider (not when it settled).</summary>
    public DateTime? RefundedAtUtc { get; private set; }

    // Navigation
    public Order Order { get; private set; } = null!;

    // -----------------------------------------------------------------------
    // Behaviour
    // -----------------------------------------------------------------------

    public void SetProviderOrderId(string providerOrderId)
        => ProviderOrderId = providerOrderId;

    public void MarkAuthorized(string providerPaymentId)
    {
        ProviderPaymentId = providerPaymentId;
        Status = PaymentStatus.Authorized;
    }

    public void MarkPaid(string providerPaymentId)
    {
        ProviderPaymentId = providerPaymentId;
        Status = PaymentStatus.Paid;
        PaidAt = DateTime.UtcNow;
        RaiseDomainEvent(new PaymentSucceededEvent(Id, OrderId, Amount, CurrencyCode));
    }

    public void MarkFailed(string? reason = null)
    {
        Status = PaymentStatus.Failed;
        FailureReason = reason;
        RaiseDomainEvent(new PaymentFailedEvent(Id, OrderId, reason));
    }

    public void MarkRefundPending() => Status = PaymentStatus.RefundPending;

    /// <summary>
    /// Records that the provider accepted a refund. <paramref name="amount"/> defaults to the
    /// full captured amount — the only amount this system currently supports, since partial
    /// refunds are not exposed to customers or admins.
    /// </summary>
    public void MarkRefunded(string? providerRefundId, decimal? amount = null)
    {
        Status = PaymentStatus.Refunded;
        ProviderRefundId = string.IsNullOrWhiteSpace(providerRefundId) ? null : providerRefundId.Trim();
        RefundedAmount = amount ?? Amount;
        RefundedAtUtc = DateTime.UtcNow;
    }

    /// <summary>True once a refund has been accepted by the provider for this payment.</summary>
    public bool IsRefunded => Status == PaymentStatus.Refunded || ProviderRefundId is not null;
}
