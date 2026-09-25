using KromicCommerce.Domain.Promotions;

namespace KromicCommerce.Application.Abstractions.Commerce;

/// <summary>
/// Centralised coupon validation and discount calculation service.
/// Both checkout and the public coupon-validate endpoint must use this service.
/// Never trust a discount amount supplied by the client.
///
/// A coupon validated at T=0 may not be valid at T=10m (usage exhausted, expired).
/// The checkout handler must re-validate the coupon inside the transaction.
/// </summary>
public interface IPromotionService
{
    /// <summary>
    /// Validates the coupon and computes the discount for the given cart context.
    /// All eligibility rules are checked; no side effects (usage is NOT recorded here).
    /// </summary>
    /// <param name="couponCode">Raw coupon code from the customer (normalised internally).</param>
    /// <param name="customerId">Authenticated customer ID.</param>
    /// <param name="subtotal">Server-calculated cart subtotal.</param>
    /// <param name="cartItems">Cart items with their product/category IDs for applicability checks.</param>
    /// <param name="isFirstOrder">Whether this is the customer's first order.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PromotionCalculationResult> ValidateAndCalculateAsync(
        string couponCode,
        Guid customerId,
        decimal subtotal,
        IReadOnlyList<CartItemContext> cartItems,
        bool isFirstOrder,
        CancellationToken cancellationToken);
}

/// <summary>Context passed to the promotion service for each cart item.</summary>
public sealed record CartItemContext(
    Guid ProductId,
    Guid? CategoryId,
    decimal LineTotal);

/// <summary>Immutable result of a promotion validation + calculation.</summary>
public sealed record PromotionCalculationResult(
    bool IsValid,
    string? CouponCode,
    Guid? PromotionId,
    decimal DiscountAmount,
    DiscountType? DiscountType,
    decimal EligibleSubtotal,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static PromotionCalculationResult NoPromotion() =>
        new(false, null, null, 0m, null, 0m, null, null);

    public static PromotionCalculationResult Invalid(string errorCode, string message) =>
        new(false, null, null, 0m, null, 0m, errorCode, message);

    public static PromotionCalculationResult Success(
        string couponCode, Guid promotionId, decimal discountAmount,
        DiscountType discountType, decimal eligibleSubtotal) =>
        new(true, couponCode, promotionId, discountAmount, discountType, eligibleSubtotal, null, null);
}
