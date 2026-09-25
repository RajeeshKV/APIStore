using KromicCommerce.Contracts.Promotions;
using KromicCommerce.Domain.Promotions;

namespace KromicCommerce.Application.Features.Admin.Promotions;

public sealed record CreatePromotionCommand(
    string Name, string? Description, string CouponCode,
    DiscountType DiscountType, decimal DiscountValue,
    decimal? MaxDiscountAmount, decimal? MinimumOrderAmount,
    int? UsageLimit, int? PerCustomerUsageLimit,
    DateTime? StartsAt, DateTime? ExpiresAt,
    PromotionApplicabilityType Applicability, bool IsFirstOrderOnly,
    List<Guid>? TargetProductIds, List<Guid>? TargetCategoryIds)
    : ICommand<PromotionDetailResponse>;

public sealed record UpdatePromotionCommand(
    Guid Id,
    string Name, string? Description,
    DiscountType DiscountType, decimal DiscountValue,
    decimal? MaxDiscountAmount, decimal? MinimumOrderAmount,
    int? UsageLimit, int? PerCustomerUsageLimit,
    DateTime? StartsAt, DateTime? ExpiresAt,
    PromotionApplicabilityType Applicability, bool IsFirstOrderOnly,
    List<Guid>? TargetProductIds, List<Guid>? TargetCategoryIds)
    : ICommand<PromotionDetailResponse>;

public sealed record ActivatePromotionCommand(Guid Id) : ICommand;
public sealed record DeactivatePromotionCommand(Guid Id) : ICommand;
public sealed record DeletePromotionCommand(Guid Id) : ICommand;
