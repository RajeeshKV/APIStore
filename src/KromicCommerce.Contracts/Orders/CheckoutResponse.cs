namespace KromicCommerce.Contracts.Orders;

public sealed record CheckoutResponse(
    Guid OrderId,
    string OrderNumber,
    string OrderStatus,
    string PaymentMethod,

    decimal Subtotal,
    decimal ShippingAmount,
    decimal CodFee,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal GrandTotal,
    string Currency,

    /// <summary>Applied coupon code (null when no promotion was used).</summary>
    string? AppliedCouponCode,

    /// <summary>
    /// Razorpay order ID to initialize the Razorpay checkout widget.
    /// Null for COD orders or when no payment provider order was created.
    /// </summary>
    string? ProviderOrderId,

    /// <summary>Store-configured Razorpay key ID for initializing the frontend widget.</summary>
    string? RazorpayKeyId,

    DateTime CreatedAtUtc);
