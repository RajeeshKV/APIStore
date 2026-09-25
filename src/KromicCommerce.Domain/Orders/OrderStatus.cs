namespace KromicCommerce.Domain.Orders;

/// <summary>
/// Order lifecycle status. Persisted as strings.
/// State machine enforced by Order domain methods — transitions not in the allowed
/// set are rejected with a domain error.
/// </summary>
public enum OrderStatus
{
    PendingPayment,
    PaymentProcessing,
    Confirmed,
    Processing,
    Packed,
    Shipped,
    Delivered,
    Cancelled,
    Failed,
    RefundPending,
    Refunded
}
