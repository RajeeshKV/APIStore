namespace KromicCommerce.Domain.Orders;

/// <summary>
/// Order lifecycle status. Persisted as strings.
/// State machine enforced by Order domain methods — transitions not in the allowed
/// set are rejected with a domain error.
///
/// Lifecycle:
///   OrderPlaced      — customer submitted the order; merchant review pending
///   Confirmed        — merchant verified stock and accepted the order
///   Processing       — order is being prepared/packed
///   Packed           — ready for pickup/handover to courier
///   Shipped          — handed to courier; tracking available
///   Delivered        — delivered to customer
///   Cancelled        — cancelled by customer or admin
///   Failed           — payment or system failure
///   RefundPending    — refund initiated, awaiting provider confirmation
///   Refunded         — refund complete
///
///   PendingPayment   — Razorpay: order created, awaiting payment capture
///   PaymentProcessing — Razorpay: payment in processing state
/// </summary>
public enum OrderStatus
{
    /// <summary>
    /// Customer has successfully submitted the order.
    /// Merchant has not yet confirmed stock availability or fulfillment readiness.
    /// All new orders start here regardless of payment method.
    /// </summary>
    OrderPlaced,

    /// <summary>Razorpay online-payment flow — awaiting customer payment capture.</summary>
    PendingPayment,

    /// <summary>Razorpay — payment is being processed by the payment gateway.</summary>
    PaymentProcessing,

    /// <summary>
    /// Merchant has verified the order and confirmed items are available.
    /// Order is ready for fulfillment.
    /// </summary>
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
