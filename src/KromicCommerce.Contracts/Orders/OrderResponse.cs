using KromicCommerce.Domain.Orders;

namespace KromicCommerce.Contracts.Orders;

public sealed record OrderResponse(
    Guid Id,
    string OrderNumber,
    OrderStatus Status,
    PaymentMethod PaymentMethod,

    decimal Subtotal,
    decimal ShippingAmount,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal GrandTotal,
    string Currency,

    ShippingAddressDto ShippingAddress,
    IReadOnlyList<OrderItemResponse> Items,

    string? TrackingNumber,
    string? TrackingProvider,
    string? CancellationReason,

    DateTime? PaidAt,
    DateTime? ShippedAt,
    DateTime? DeliveredAt,
    DateTime? CancelledAt,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
