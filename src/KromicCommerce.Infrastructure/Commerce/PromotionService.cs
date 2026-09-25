using KromicCommerce.Application.Abstractions.Commerce;
using KromicCommerce.Application.Abstractions.Data;
using KromicCommerce.Domain.Promotions;

namespace KromicCommerce.Infrastructure.Commerce;

/// <summary>
/// Validates a coupon code and computes the applicable discount.
/// All eligibility rules are evaluated in this single method.
/// No usage is recorded here — that happens inside the checkout transaction.
///
/// Concurrency note: usage counts are read inside the calling transaction to
/// prevent race conditions at the margin. The checkout handler re-validates
/// inside the same DB transaction with a row lock on the Promotion row.
/// </summary>
internal sealed class PromotionService(IApplicationDbContext db) : IPromotionService
{
    public async Task<PromotionCalculationResult> ValidateAndCalculateAsync(
        string couponCode,
        Guid customerId,
        decimal subtotal,
        IReadOnlyList<CartItemContext> cartItems,
        bool isFirstOrder,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(couponCode))
            return PromotionCalculationResult.NoPromotion();

        var normalisedCode = Promotion.NormaliseCouponCode(couponCode);

        // Load promotion with its target products and categories
        var promotion = await db.Promotions
            .Include(p => p.Products)
            .Include(p => p.Categories)
            .FirstOrDefaultAsync(p => p.CouponCode == normalisedCode, cancellationToken);

        if (promotion is null)
            return PromotionCalculationResult.Invalid(
                "COUPON_NOT_FOUND", $"Coupon '{couponCode}' does not exist.");

        // Time window + active check
        if (!promotion.IsCurrentlyValid(DateTime.UtcNow))
            return PromotionCalculationResult.Invalid(
                "COUPON_INACTIVE", "This coupon is not currently active.");

        // Global usage limit
        if (!promotion.HasRemainingUsage())
            return PromotionCalculationResult.Invalid(
                "COUPON_EXHAUSTED", "This coupon has reached its usage limit.");

        // Per-customer usage limit
        if (promotion.PerCustomerUsageLimit.HasValue)
        {
            var customerUsageCount = await db.PromotionUsages
                .CountAsync(u => u.PromotionId == promotion.Id && u.CustomerId == customerId,
                    cancellationToken);

            if (customerUsageCount >= promotion.PerCustomerUsageLimit.Value)
                return PromotionCalculationResult.Invalid(
                    "COUPON_CUSTOMER_LIMIT",
                    "You have already used this coupon the maximum number of times.");
        }

        // First-order restriction
        if (promotion.IsFirstOrderOnly && !isFirstOrder)
            return PromotionCalculationResult.Invalid(
                "COUPON_FIRST_ORDER_ONLY", "This coupon is only valid on your first order.");

        // Minimum order amount
        if (promotion.MinimumOrderAmount.HasValue && subtotal < promotion.MinimumOrderAmount.Value)
            return PromotionCalculationResult.Invalid(
                "COUPON_MINIMUM_NOT_MET",
                $"A minimum order of {promotion.MinimumOrderAmount.Value:0.##} is required for this coupon.");

        // Determine the eligible subtotal based on applicability
        decimal eligibleSubtotal = promotion.Applicability switch
        {
            PromotionApplicabilityType.EntireOrder =>
                subtotal,

            PromotionApplicabilityType.SpecificProducts =>
                cartItems
                    .Where(ci => promotion.Products.Any(pp => pp.ProductId == ci.ProductId))
                    .Sum(ci => ci.LineTotal),

            PromotionApplicabilityType.SpecificCategories =>
                cartItems
                    .Where(ci => ci.CategoryId.HasValue &&
                                 promotion.Categories.Any(pc => pc.CategoryId == ci.CategoryId.Value))
                    .Sum(ci => ci.LineTotal),

            _ => 0m
        };

        if (eligibleSubtotal <= 0m)
            return PromotionCalculationResult.Invalid(
                "COUPON_NOT_APPLICABLE",
                "This coupon does not apply to any items in your cart.");

        var discount = promotion.CalculateDiscount(eligibleSubtotal);

        return PromotionCalculationResult.Success(
            normalisedCode, promotion.Id, discount,
            promotion.DiscountType, eligibleSubtotal);
    }
}
