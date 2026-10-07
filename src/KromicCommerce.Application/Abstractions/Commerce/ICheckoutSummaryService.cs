using KromicCommerce.Domain.Orders;

using KromicCommerce.Contracts.Catalog;

namespace KromicCommerce.Application.Abstractions.Commerce;

/// <summary>
/// Authoritative pre-payment pricing engine.
///
/// Every monetary value a customer sees before payment — and every value the order is
/// created with — is produced here. The summary endpoint and the checkout command share this
/// one service so the figure shown at checkout and the figure charged can never diverge.
///
/// The backend is the source of truth: no amount on any request DTO is ever read.
/// </summary>
public interface ICheckoutSummaryService
{
    /// <summary>
    /// Calculates the full checkout summary for a customer's cart.
    ///
    /// <paramref name="couponCode"/> overrides the coupon stored on the cart when supplied.
    /// A coupon that is present but invalid produces a summary with
    /// <see cref="CheckoutSummary.CouponErrorCode"/> set rather than a hard failure, so the
    /// UI can render the cart without the coupon and explain why.
    /// </summary>
    Task<Result<CheckoutSummary>> CalculateAsync(
        CheckoutSummaryRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Inputs to a checkout summary calculation. Contains no monetary values.</summary>
public sealed record CheckoutSummaryRequest(
    Guid CustomerId,

    /// <summary>Payment method being priced. Null means "not chosen yet" (no COD fee).</summary>
    PaymentMethod? PaymentMethod,

    /// <summary>Overrides the coupon stored on the cart when supplied.</summary>
    string? CouponCode = null);

/// <summary>One priced cart line, ready for display.</summary>
public sealed record CheckoutSummaryItem(
    Guid CartItemId,
    Guid ProductId,
    Guid? VariantId,
    string ProductName,
    string ProductSlug,
    string? VariantDescription,
    string? Sku,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal,
    StockAvailability StockAvailability,
    bool CanPurchase,
    string? PrimaryImageUrl,

    /// <summary>
    /// Resolved variant attributes for display (e.g., Color: Orange, Storage: 256GB).
    /// Empty when the product has no variants or the variant has no attribute values.
    /// </summary>
    IReadOnlyList<VariantAttributeValueResponse>? VariantAttributes = null);

/// <summary>Whether a payment method can be offered for the current cart and store config.</summary>
public sealed record CheckoutPaymentMethodAvailability(
    PaymentMethod Method,
    bool IsAvailable,
    string? UnavailableReason);

/// <summary>
/// Complete, recalculated checkout summary. Every field is server-derived.
/// </summary>
public sealed record CheckoutSummary(
    // Lines
    IReadOnlyList<CheckoutSummaryItem> Items,

    // Totals — the payable breakdown
    decimal Subtotal,
    decimal DiscountAmount,
    decimal TaxAmount,
    string TaxLabel,
    bool IsPriceInclusive,
    decimal ShippingAmount,
    decimal CodFee,
    decimal GrandTotal,
    string CurrencyCode,

    // Coupon
    string? AppliedCouponCode,
    Guid? AppliedPromotionId,
    DiscountType? DiscountType,
    decimal EligibleSubtotal,
    string? CouponErrorCode,
    string? CouponErrorMessage,

    // Shipping / COD configuration as applied
    bool IsFreeShipping,
    decimal? FreeShippingThreshold,
    decimal RemainingForFreeShipping,
    bool IsCodAvailable,
    DeliveryEstimateDto? DeliveryEstimate,

    // Payment method availability
    bool IsRazorpayConfigured,
    IReadOnlyList<CheckoutPaymentMethodAvailability> PaymentMethods,

    // Readiness
    bool IsReadyToCheckout,
    IReadOnlyList<string> BlockingReasons,

    // True when this summary is a valid, complete quote that the order may be created from.
    bool IsQuotable)
{
    /// <summary>Items priced but with one or more lines unavailable to purchase.</summary>
    public bool HasStockIssues =>
        Items.Any(i => !i.CanPurchase);
}
