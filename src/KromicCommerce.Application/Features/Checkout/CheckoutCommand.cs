namespace KromicCommerce.Application.Features.Checkout;

public sealed record CheckoutCommand(
    Guid CustomerId,
    ShippingAddressDto ShippingAddress,
    string PaymentMethod,
    string? CouponCode,
    string? IdempotencyKey) : ICommand<CheckoutResponse>;
