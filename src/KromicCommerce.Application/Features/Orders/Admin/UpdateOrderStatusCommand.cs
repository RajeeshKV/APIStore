using KromicCommerce.Contracts.Orders;
using KromicCommerce.Domain.Orders;

namespace KromicCommerce.Application.Features.Orders.Admin;

public sealed record UpdateOrderStatusCommand(
    Guid OrderId,
    OrderStatus Status,
    string? TrackingNumber,
    string? TrackingProvider,
    string? Reason) : ICommand<OrderResponse>;
