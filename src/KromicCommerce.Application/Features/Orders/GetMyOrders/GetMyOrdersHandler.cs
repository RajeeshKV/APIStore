namespace KromicCommerce.Application.Features.Orders.GetMyOrders;

internal sealed class GetMyOrdersHandler(IApplicationDbContext db)
    : IQueryHandler<GetMyOrdersQuery, PagedResponse<OrderSummaryResponse>>
{
    public async Task<Result<PagedResponse<OrderSummaryResponse>>> Handle(
        GetMyOrdersQuery query, CancellationToken ct)
    {
        var pageSize = Math.Clamp(query.PageSize, 1, 50);
        var page = Math.Max(query.Page, 1);

        var q = db.Orders.AsNoTracking()
            .Where(o => o.CustomerId == query.CustomerId)
            .OrderByDescending(o => o.CreatedAtUtc);

        var total = await q.CountAsync(ct);
        var orders = await q.Skip((page - 1) * pageSize).Take(pageSize)
            .Include(o => o.Items)
            .ToListAsync(ct);

        var mapped = orders.Select(o => new OrderSummaryResponse(
            o.Id, o.OrderNumber, o.Status.ToString(), o.PaymentMethod.ToString(),
            o.GrandTotal, o.CurrencyCode,
            o.Items.Count,
            o.CreatedAtUtc)).ToList();

        return Result.Success(new PagedResponse<OrderSummaryResponse>(mapped, page, pageSize, total));
    }
}

internal sealed class GetMyOrderByIdHandler(IApplicationDbContext db)
    : IQueryHandler<GetMyOrderByIdQuery, OrderResponse>
{
    public async Task<Result<OrderResponse>> Handle(
        GetMyOrderByIdQuery query, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == query.OrderId && o.CustomerId == query.CustomerId, ct);

        if (order is null)
            return Result.Failure<OrderResponse>(Error.NotFound("ORDER_NOT_FOUND", "Order not found."));

        return Result.Success(MapToResponse(order));
    }

    internal static OrderResponse MapToResponse(Order o) =>
        new(o.Id, o.OrderNumber, o.Status.ToString(), o.PaymentMethod.ToString(),
            o.Subtotal, o.ShippingAmount, o.DiscountAmount, o.TaxAmount, o.GrandTotal, o.CurrencyCode,
            new ShippingAddressDto(
                o.ShippingAddress.FullName, o.ShippingAddress.Phone,
                o.ShippingAddress.AddressLine1, o.ShippingAddress.AddressLine2,
                o.ShippingAddress.City, o.ShippingAddress.State,
                o.ShippingAddress.PostalCode, o.ShippingAddress.Country),
            o.Items.Select(i => new OrderItemResponse(
                i.Id, i.ProductId, i.VariantId, i.ProductName,
                i.VariantDescription, i.Sku, i.UnitPrice, i.Quantity, i.LineTotal))
                .ToList(),
            o.TrackingNumber, o.TrackingProvider, o.CancellationReason,
            o.PaidAt, o.ShippedAt, o.DeliveredAt, o.CancelledAt,
            o.CreatedAtUtc, o.UpdatedAtUtc);
}
