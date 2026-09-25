namespace KromicCommerce.Contracts.Orders;

/// <summary>
/// Customer-supplied checkout data.
/// The server rejects any attempt to supply prices, discounts, shipping, or totals.
/// Only shipping address and payment method are accepted from the client.
/// </summary>
public sealed record CheckoutRequest(
    ShippingAddressDto ShippingAddress,

    /// <summary>"Razorpay" or "CashOnDelivery". COD availability is validated server-side.</summary>
    string PaymentMethod,

    /// <summary>Optional coupon code to apply a promotion discount. Validated server-side.</summary>
    string? CouponCode = null,

    /// <summary>Optional idempotency key supplied by the client to prevent duplicate orders on retry.</summary>
    string? IdempotencyKey = null);
