using KromicCommerce.Domain.Orders;

namespace KromicCommerce.Contracts.Orders;

public sealed record OrderSummaryResponse(
    Guid Id,
    string OrderNumber,
    OrderStatus Status,
    PaymentMethod PaymentMethod,
    decimal GrandTotal,
    string Currency,
    int ItemCount,
    DateTime CreatedAtUtc);
