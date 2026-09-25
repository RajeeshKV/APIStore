namespace KromicCommerce.Application.Features.Orders.GetMyOrders;

public sealed record GetMyOrdersQuery(Guid CustomerId, int Page = 1, int PageSize = 10)
    : IQuery<PagedResponse<OrderSummaryResponse>>;

public sealed record GetMyOrderByIdQuery(Guid OrderId, Guid CustomerId)
    : IQuery<OrderResponse>;
