using KromicCommerce.Domain.Orders;

namespace KromicCommerce.Contracts.Orders;

public sealed record UpdateOrderStatusRequest(
    OrderStatus Status,
    string? TrackingNumber = null,
    string? TrackingProvider = null,
    string? Reason = null);
