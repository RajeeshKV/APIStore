using KromicCommerce.Domain.Orders;

namespace KromicCommerce.Contracts.Orders;

public sealed record AdminOrderQueryRequest(
    int Page = 1,
    int PageSize = 20,
    string? Search = null,        // order number or customer email
    OrderStatus? Status = null,
    PaymentStatus? PaymentStatus = null,
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    string? SortBy = null,        // whitelisted: "created_at", "grand_total", "status"
    string SortDirection = "desc");
