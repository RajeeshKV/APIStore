using KromicCommerce.Application.Features.Orders.GetMyOrders;

namespace KromicCommerce.Application.Features.Orders.Admin;

internal sealed class GetAdminOrdersHandler(IApplicationDbContext db)
    : IQueryHandler<GetAdminOrdersQuery, PagedResponse<OrderSummaryResponse>>
{
    private static readonly HashSet<string> AllowedSortFields =
        ["created_at", "grand_total", "status"];

    public async Task<Result<PagedResponse<OrderSummaryResponse>>> Handle(
        GetAdminOrdersQuery query, CancellationToken ct)
    {
        var req = query.Request;
        var pageSize = Math.Clamp(req.PageSize, 1, 100);
        var page = Math.Max(req.Page, 1);

        if (req.SortBy is not null && !AllowedSortFields.Contains(req.SortBy.ToLower()))
            return Result.Failure<PagedResponse<OrderSummaryResponse>>(
                Error.Validation("INVALID_SORT", $"Sort field '{req.SortBy}' is not allowed."));

        var q = db.Orders.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(req.Search))
        {
            var term = req.Search.Trim().ToLower();
            q = q.Where(o => o.OrderNumber.ToLower().Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(req.Status)
            && Enum.TryParse<OrderStatus>(req.Status, out var status))
            q = q.Where(o => o.Status == status);

        if (req.FromDate.HasValue) q = q.Where(o => o.CreatedAtUtc >= req.FromDate.Value);
        if (req.ToDate.HasValue) q = q.Where(o => o.CreatedAtUtc <= req.ToDate.Value);

        var total = await q.CountAsync(ct);
        var desc = !string.Equals(req.SortDirection, "asc", StringComparison.OrdinalIgnoreCase);
        q = req.SortBy?.ToLower() switch
        {
            "grand_total" => desc ? q.OrderByDescending(o => o.GrandTotal) : q.OrderBy(o => o.GrandTotal),
            "status" => desc ? q.OrderByDescending(o => o.Status) : q.OrderBy(o => o.Status),
            _ => desc ? q.OrderByDescending(o => o.CreatedAtUtc) : q.OrderBy(o => o.CreatedAtUtc)
        };

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

internal sealed class GetAdminOrderByIdHandler(IApplicationDbContext db)
    : IQueryHandler<GetAdminOrderByIdQuery, OrderResponse>
{
    public async Task<Result<OrderResponse>> Handle(
        GetAdminOrderByIdQuery query, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == query.OrderId, ct);

        if (order is null)
            return Result.Failure<OrderResponse>(Error.NotFound("ORDER_NOT_FOUND", "Order not found."));

        return Result.Success(GetMyOrderByIdHandler.MapToResponse(order));
    }
}
