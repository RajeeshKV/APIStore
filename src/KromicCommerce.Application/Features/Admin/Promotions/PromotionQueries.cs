using KromicCommerce.Contracts.Common;
using KromicCommerce.Contracts.Promotions;

namespace KromicCommerce.Application.Features.Admin.Promotions;

public sealed record GetPromotionsQuery(
    int Page = 1,
    int PageSize = 20,
    bool? IsActive = null,
    string? CouponCode = null,
    DateTime? StartFrom = null,
    DateTime? StartTo = null,
    string SortBy = "CreatedAtUtc",
    bool Descending = true)
    : IQuery<PagedResponse<PromotionSummaryResponse>>;

public sealed record GetPromotionByIdQuery(Guid Id) : IQuery<PromotionDetailResponse>;
