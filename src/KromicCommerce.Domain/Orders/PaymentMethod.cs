namespace KromicCommerce.Domain.Orders;

/// <summary>
/// Payment method selected by the customer at checkout.
/// Persisted as string. COD availability is validated server-side against
/// BusinessSettings.Delivery.CodEnabled — the frontend cannot override this.
/// </summary>
public enum PaymentMethod
{
    Razorpay,
    CashOnDelivery
}
