namespace KromicCommerce.Application.Features.Orders.Admin;

public sealed record GetAdminOrdersQuery(AdminOrderQueryRequest Request)
    : IQuery<PagedResponse<OrderSummaryResponse>>;

public sealed record GetAdminOrderByIdQuery(Guid OrderId)
    : IQuery<OrderResponse>;
