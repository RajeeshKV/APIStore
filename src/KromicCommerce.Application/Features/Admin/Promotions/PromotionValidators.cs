using KromicCommerce.Domain.Promotions;

namespace KromicCommerce.Application.Features.Admin.Promotions;

internal sealed class CreatePromotionValidator : AbstractValidator<CreatePromotionCommand>
{
    public CreatePromotionValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000).When(x => x.Description is not null);
        RuleFor(x => x.CouponCode)
            .NotEmpty()
            .MinimumLength(3).WithMessage("Coupon code must be at least 3 characters.")
            .MaximumLength(50);
        RuleFor(x => x.DiscountValue)
            .GreaterThan(0m).WithMessage("Discount value must be > 0.")
            .LessThanOrEqualTo(100m)
            .When(x => x.DiscountType == DiscountType.Percentage)
            .WithMessage("Percentage discount must be <= 100.");
        RuleFor(x => x.DiscountValue)
            .GreaterThan(0m)
            .When(x => x.DiscountType == DiscountType.FixedAmount);
        RuleFor(x => x.MaxDiscountAmount)
            .GreaterThanOrEqualTo(0m).When(x => x.MaxDiscountAmount.HasValue);
        RuleFor(x => x.MinimumOrderAmount)
            .GreaterThanOrEqualTo(0m).When(x => x.MinimumOrderAmount.HasValue);
        RuleFor(x => x.UsageLimit)
            .GreaterThanOrEqualTo(1).When(x => x.UsageLimit.HasValue);
        RuleFor(x => x.PerCustomerUsageLimit)
            .GreaterThanOrEqualTo(1).When(x => x.PerCustomerUsageLimit.HasValue);
        RuleFor(x => x.ExpiresAt)
            .GreaterThan(x => x.StartsAt!.Value)
            .When(x => x.StartsAt.HasValue && x.ExpiresAt.HasValue)
            .WithMessage("Expiry must be after start date.");
        RuleFor(x => x.TargetProductIds)
            .NotEmpty()
            .When(x => x.Applicability == PromotionApplicabilityType.SpecificProducts)
            .WithMessage("At least one target product is required for SpecificProducts applicability.");
        RuleFor(x => x.TargetCategoryIds)
            .NotEmpty()
            .When(x => x.Applicability == PromotionApplicabilityType.SpecificCategories)
            .WithMessage("At least one target category is required for SpecificCategories applicability.");
    }
}

internal sealed class UpdatePromotionValidator : AbstractValidator<UpdatePromotionCommand>
{
    public UpdatePromotionValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000).When(x => x.Description is not null);
        RuleFor(x => x.DiscountValue)
            .GreaterThan(0m)
            .LessThanOrEqualTo(100m)
            .When(x => x.DiscountType == DiscountType.Percentage);
        RuleFor(x => x.DiscountValue)
            .GreaterThan(0m)
            .When(x => x.DiscountType == DiscountType.FixedAmount);
        RuleFor(x => x.MaxDiscountAmount)
            .GreaterThanOrEqualTo(0m).When(x => x.MaxDiscountAmount.HasValue);
        RuleFor(x => x.MinimumOrderAmount)
            .GreaterThanOrEqualTo(0m).When(x => x.MinimumOrderAmount.HasValue);
        RuleFor(x => x.UsageLimit)
            .GreaterThanOrEqualTo(1).When(x => x.UsageLimit.HasValue);
        RuleFor(x => x.PerCustomerUsageLimit)
            .GreaterThanOrEqualTo(1).When(x => x.PerCustomerUsageLimit.HasValue);
        RuleFor(x => x.ExpiresAt)
            .GreaterThan(x => x.StartsAt!.Value)
            .When(x => x.StartsAt.HasValue && x.ExpiresAt.HasValue)
            .WithMessage("Expiry must be after start date.");
        RuleFor(x => x.TargetProductIds)
            .NotEmpty()
            .When(x => x.Applicability == PromotionApplicabilityType.SpecificProducts);
        RuleFor(x => x.TargetCategoryIds)
            .NotEmpty()
            .When(x => x.Applicability == PromotionApplicabilityType.SpecificCategories);
    }
}

internal sealed class GetPromotionsValidator : AbstractValidator<GetPromotionsQuery>
{
    private static readonly HashSet<string> AllowedSortFields =
        ["Name", "CouponCode", "CreatedAtUtc", "StartsAt", "ExpiresAt", "UsageCount", "IsActive"];

    public GetPromotionsValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.SortBy)
            .Must(s => AllowedSortFields.Contains(s))
            .WithMessage($"SortBy must be one of: {string.Join(", ", AllowedSortFields)}.");
        RuleFor(x => x.StartTo)
            .GreaterThan(x => x.StartFrom!.Value)
            .When(x => x.StartFrom.HasValue && x.StartTo.HasValue);
    }
}
