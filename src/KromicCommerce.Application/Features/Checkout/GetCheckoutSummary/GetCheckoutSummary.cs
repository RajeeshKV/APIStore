namespace KromicCommerce.Application.Features.Checkout.GetCheckoutSummary;

/// <summary>
/// Returns the complete, server-calculated checkout summary for the current cart.
///
/// This is the endpoint the checkout screen must call before payment. It never mutates state:
/// applying or removing a coupon is done through the dedicated cart coupon endpoints.
/// </summary>
public sealed record GetCheckoutSummaryQuery(
    Guid CustomerId,
    PaymentMethod? PaymentMethod = null,

    /// <summary>
    /// Optional coupon code to price. When supplied it overrides the coupon stored on the cart
    /// for this calculation only. Omit to price with the cart's own coupon.
    /// </summary>
    string? CouponCode = null) : IQuery<CheckoutSummaryResponse>;

internal sealed class GetCheckoutSummaryValidator : AbstractValidator<GetCheckoutSummaryQuery>
{
    public GetCheckoutSummaryValidator()
    {
        RuleFor(x => x.CustomerId)
            .NotEmpty().WithMessage("Customer is required.");

        RuleFor(x => x.PaymentMethod)
            .IsInEnum().WithMessage("Payment method must be a valid value.")
            .When(x => x.PaymentMethod.HasValue);

        RuleFor(x => x.CouponCode)
            .MaximumLength(50).WithMessage("Coupon code must not exceed 50 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.CouponCode));
    }
}

internal sealed class GetCheckoutSummaryHandler(ICheckoutSummaryService summaryService)
    : IQueryHandler<GetCheckoutSummaryQuery, CheckoutSummaryResponse>
{
    public async Task<Result<CheckoutSummaryResponse>> Handle(
        GetCheckoutSummaryQuery query, CancellationToken cancellationToken)
    {
        var result = await summaryService.CalculateAsync(
            new CheckoutSummaryRequest(query.CustomerId, query.PaymentMethod, query.CouponCode),
            cancellationToken);

        if (!result.IsSuccess)
            return Result.Failure<CheckoutSummaryResponse>(result.Error);

        return Result.Success(CheckoutSummaryMapper.Map(result.Value));
    }
}

/// <summary>
/// Maps the internal pricing summary to the public contract.
/// Blocking reasons are turned into a code plus a human-readable message so clients can
/// display them without maintaining their own copy of the rules.
/// </summary>
internal static class CheckoutSummaryMapper
{
    public static CheckoutSummaryResponse Map(CheckoutSummary s) => new(
        Items: s.Items.Select(MapItem).ToList(),
        Subtotal: s.Subtotal,
        DiscountAmount: s.DiscountAmount,
        TaxAmount: s.TaxAmount,
        TaxLabel: s.TaxLabel,
        IsPriceInclusive: s.IsPriceInclusive,
        ShippingAmount: s.ShippingAmount,
        CodFee: s.CodFee,
        GrandTotal: s.GrandTotal,
        Currency: s.CurrencyCode,
        AppliedCouponCode: s.AppliedCouponCode,
        DiscountType: s.DiscountType,
        EligibleSubtotal: s.EligibleSubtotal,
        CouponErrorCode: s.CouponErrorCode,
        CouponErrorMessage: s.CouponErrorMessage,
        IsFreeShipping: s.IsFreeShipping,
        FreeShippingThreshold: s.FreeShippingThreshold,
        RemainingForFreeShipping: s.RemainingForFreeShipping,
        IsCodAvailable: s.IsCodAvailable,
        DeliveryEstimate: s.DeliveryEstimate,
        IsRazorpayConfigured: s.IsRazorpayConfigured,
        PaymentMethods: s.PaymentMethods
            .Select(m => new CheckoutPaymentMethodResponse(m.Method, m.IsAvailable, m.UnavailableReason))
            .ToList(),
        IsReadyToCheckout: s.IsReadyToCheckout,
        BlockingReasons: s.BlockingReasons.Select(Describe).ToList());

    private static CheckoutSummaryItemResponse MapItem(CheckoutSummaryItem i) => new(
        i.CartItemId, i.ProductId, i.VariantId,
        i.ProductName, i.ProductSlug,
        i.VariantDescription, i.Sku,
        i.UnitPrice, i.Quantity, i.LineTotal,
        i.StockAvailability, i.CanPurchase, i.PrimaryImageUrl,
        VariantAttributes: i.VariantAttributes);

    /// <summary>
    /// Human-readable text for a blocking reason code. Kept beside the codes so the copy and
    /// the rule live together; a client that prefers its own wording may ignore the message.
    /// </summary>
    private static CheckoutBlockingReasonResponse Describe(string reason) => reason switch
    {
        "CART_EMPTY" =>
            new(reason, "Your cart is empty."),
        "COD_NOT_AVAILABLE" =>
            new(reason, "Cash on delivery is not available for this store."),
        "RAZORPAY_NOT_CONFIGURED" =>
            new(reason, "Online payments are temporarily unavailable."),
        var r when r.StartsWith("PRODUCT_UNAVAILABLE:", StringComparison.Ordinal) =>
            new(reason, "A product in your cart is no longer available."),
        var r when r.StartsWith("VARIANT_UNAVAILABLE:", StringComparison.Ordinal) =>
            new(reason, "A selected product option is no longer available."),
        var r when r.StartsWith("INSUFFICIENT_STOCK:", StringComparison.Ordinal) =>
            new(reason, "A product in your cart no longer has enough stock."),
        _ => new(reason, "This order cannot be placed as it stands.")
    };
}
