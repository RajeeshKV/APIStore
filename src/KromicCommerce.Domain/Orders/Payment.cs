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
    public void MarkRefunded()      => Status = PaymentStatus.Refunded;
}
