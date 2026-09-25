namespace KromicCommerce.Contracts.Promotions;

// -----------------------------------------------------------------------
// Admin requests
// -----------------------------------------------------------------------

public sealed record CreatePromotionRequest(
    string Name,
    string? Description,
    string CouponCode,
    string DiscountType,           // "Percentage" | "FixedAmount"
    decimal DiscountValue,
    decimal? MaxDiscountAmount,
    decimal? MinimumOrderAmount,
    int? UsageLimit,
    int? PerCustomerUsageLimit,
    DateTime? StartsAt,
    DateTime? ExpiresAt,
    string Applicability,          // "EntireOrder" | "SpecificProducts" | "SpecificCategories"
    bool IsFirstOrderOnly,
    List<Guid>? TargetProductIds,
    List<Guid>? TargetCategoryIds);

public sealed record UpdatePromotionRequest(
    string Name,
    string? Description,
    string DiscountType,
    decimal DiscountValue,
    decimal? MaxDiscountAmount,
    decimal? MinimumOrderAmount,
    int? UsageLimit,
    int? PerCustomerUsageLimit,
    DateTime? StartsAt,
    DateTime? ExpiresAt,
    string Applicability,
    bool IsFirstOrderOnly,
    List<Guid>? TargetProductIds,
    List<Guid>? TargetCategoryIds);

// -----------------------------------------------------------------------
// Admin responses
// -----------------------------------------------------------------------

public sealed record PromotionSummaryResponse(
    Guid Id,
    string Name,
    string CouponCode,
    string DiscountType,
    decimal DiscountValue,
    decimal? MaxDiscountAmount,
    bool IsActive,
    int UsageCount,
    int? UsageLimit,
    DateTime? StartsAt,
    DateTime? ExpiresAt,
    string Applicability,
    DateTime CreatedAtUtc);

public sealed record PromotionDetailResponse(
    Guid Id,
    string Name,
    string? Description,
    string CouponCode,
    string DiscountType,
    decimal DiscountValue,
    decimal? MaxDiscountAmount,
    decimal? MinimumOrderAmount,
    int? UsageLimit,
    int? PerCustomerUsageLimit,
    DateTime? StartsAt,
    DateTime? ExpiresAt,
    string Applicability,
    bool IsFirstOrderOnly,
    bool IsActive,
    int UsageCount,
    List<Guid> TargetProductIds,
    List<Guid> TargetCategoryIds,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

// -----------------------------------------------------------------------
// Customer-facing coupon validation
// -----------------------------------------------------------------------

public sealed record ValidateCouponRequest(
    string CouponCode);

public sealed record CouponValidationResponse(
    bool IsValid,
    string? CouponCode,
    decimal DiscountAmount,
    string? DiscountType,
    decimal EligibleSubtotal,
    string? ErrorCode,
    string? ErrorMessage);
