using KromicCommerce.Contracts.Orders;

namespace KromicCommerce.Application.Features.Orders.CancelOrder;

public sealed record CancelOrderCommand(
    Guid OrderId,
    Guid CustomerId,
    string? Reason) : ICommand<OrderResponse>;
