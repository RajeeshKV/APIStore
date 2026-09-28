using KromicCommerce.Domain.Orders;

namespace KromicCommerce.Application.Features.Checkout;

public sealed record CheckoutCommand(
    Guid CustomerId,
    ShippingAddressDto ShippingAddress,
    PaymentMethod PaymentMethod,
    string? CouponCode,
    string? IdempotencyKey) : ICommand<CheckoutResponse>;
