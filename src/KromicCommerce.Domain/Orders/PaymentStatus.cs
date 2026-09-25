namespace KromicCommerce.Domain.Orders;

/// <summary>
/// Payment status — separate from order status.
/// Persisted as strings. State transitions enforced by the Payment entity.
/// </summary>
public enum PaymentStatus
{
    Pending,
    Authorized,
    Paid,
    Failed,
    RefundPending,
    Refunded
}
