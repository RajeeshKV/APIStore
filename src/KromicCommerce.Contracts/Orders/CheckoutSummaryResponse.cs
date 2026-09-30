using KromicCommerce.Contracts.Catalog;
using KromicCommerce.Domain.Orders;
using KromicCommerce.Domain.Promotions;

namespace KromicCommerce.Contracts.Orders;

/// <summary>
/// Complete, server-calculated checkout summary. Return this to the customer before payment.
///
/// Every field is derived by the backend from live catalog data, the cart, the stored coupon
/// and the shipping/tax configuration. The client must never compute or display its own total.
/// </summary>
public sealed record CheckoutSummaryResponse(
    // -----------------------------------------------------------------------
    // Lines
    // -----------------------------------------------------------------------
    IReadOnlyList<CheckoutSummaryItemResponse> Items,

    // -----------------------------------------------------------------------
    // Payable breakdown
    // -----------------------------------------------------------------------
    decimal Subtotal,
    decimal DiscountAmount,
    decimal TaxAmount,
    string TaxLabel,

    /// <summary>
    /// True when the catalogue price already contains tax. When true, TaxAmount is
    /// informational and is NOT added on top of Subtotal.
    /// </summary>
    bool IsPriceInclusive,

    decimal ShippingAmount,
    decimal CodFee,

    /// <summary>Amount the customer will be charged. Excludes TaxAmount when IsPriceInclusive.</summary>
    decimal GrandTotal,
    string Currency,

    // -----------------------------------------------------------------------
    // Coupon
    // -----------------------------------------------------------------------

    /// <summary>
    /// The coupon code in effect for this quote.
    ///
    /// IMPORTANT: this echoes the code that was REQUESTED, which is not always the same as a
    /// code that was GRANTED. When a coupon is rejected this field still carries the submitted
    /// code, because the checkout command needs it to detect that the customer expected a
    /// discount and must fail rather than silently charge full price.
    ///
    /// To decide whether a coupon is actually applied, test
    /// <see cref="CouponErrorCode"/> being null — not this field being non-null. Rendering
    /// "coupon applied" off a non-null value here will show a discount that is not there.
    /// </summary>
    string? AppliedCouponCode,
    DiscountType? DiscountType,

    /// <summary>Cart subtotal the coupon was evaluated against (may be less than Subtotal).</summary>
    decimal EligibleSubtotal,

    /// <summary>
    /// Populated when a coupon was supplied but rejected. The summary is still returned with
    /// DiscountAmount 0 so the cart can be rendered; treat the coupon as not applied.
    /// </summary>
    string? CouponErrorCode,
    string? CouponErrorMessage,

    // -----------------------------------------------------------------------
    // Shipping / cash-on-delivery configuration as applied
    // -----------------------------------------------------------------------
    bool IsFreeShipping,
    decimal? FreeShippingThreshold,

    /// <summary>
    /// Amount still needed to qualify for free shipping. 0 when free shipping already applies
    /// or the store has no threshold configured.
    /// </summary>
    decimal RemainingForFreeShipping,

    /// <summary>True when the store currently accepts cash-on-delivery orders.</summary>
    bool IsCodAvailable,

    /// <summary>Estimated delivery window. Null when delivery estimates are not configured.</summary>
    DeliveryEstimateDto? DeliveryEstimate,

    // -----------------------------------------------------------------------
    // Payment methods
    // -----------------------------------------------------------------------
    bool IsRazorpayConfigured,
    IReadOnlyList<CheckoutPaymentMethodResponse> PaymentMethods,

    // -----------------------------------------------------------------------
    // Readiness
    // -----------------------------------------------------------------------
    bool IsReadyToCheckout,
    IReadOnlyList<CheckoutBlockingReasonResponse> BlockingReasons);

/// <summary>One priced cart line.</summary>
public sealed record CheckoutSummaryItemResponse(
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
    string? PrimaryImageUrl);

/// <summary>Availability of a single payment method for the current cart and store config.</summary>
public sealed record CheckoutPaymentMethodResponse(
    PaymentMethod Method,
    bool IsAvailable,

    /// <summary>Human-readable explanation when IsAvailable is false; null otherwise.</summary>
    string? UnavailableReason);

/// <summary>Why the checkout cannot proceed. Safe to map to a user-facing message.</summary>
public sealed record CheckoutBlockingReasonResponse(
    string Code,
    string Message);

/// <summary>Request for a checkout summary. Contains no monetary values by design.</summary>
public sealed record GetCheckoutSummaryRequest(
    PaymentMethod? PaymentMethod = null,
    string? CouponCode = null);
