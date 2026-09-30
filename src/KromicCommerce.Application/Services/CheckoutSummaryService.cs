using KromicCommerce.Application.Abstractions.Catalog;
using KromicCommerce.Application.Abstractions.Commerce;
using KromicCommerce.Application.Abstractions.Payments;
using KromicCommerce.Application.Abstractions.Store;
using KromicCommerce.Domain.Cart;
using KromicCommerce.Domain.Catalog;
using KromicCommerce.Domain.Orders;
using KromicCommerce.Domain.Promotions;
using KromicCommerce.Domain.Store;

namespace KromicCommerce.Application.Services;

/// <summary>
/// Authoritative checkout pricing engine.
///
/// This is the only place that turns a cart into a payable amount. Both
/// <c>GET /checkout/summary</c> and <c>POST /checkout</c> go through it, so the total the
/// customer is shown is by construction the total the order is created with.
///
/// It is deliberately read-only: it reserves no inventory, creates no order and records no
/// coupon usage. Applying the quote is <c>CheckoutHandler</c>'s job, and it re-derives every
/// figure through this same service so a cart that changed between the summary call and the
/// checkout call cannot be charged a stale price.
/// </summary>
internal sealed class CheckoutSummaryService(
    IApplicationDbContext db,
    IBusinessSettingsService businessSettings,
    IPromotionService promotionService,
    IShippingCalculationService shippingService,
    ITaxCalculationService taxService,
    IStorefrontStockService stockService,
    IPaymentGateway paymentGateway,
    ILogger<CheckoutSummaryService> logger)
    : ICheckoutSummaryService
{
    public async Task<Result<CheckoutSummary>> CalculateAsync(
        CheckoutSummaryRequest request, CancellationToken cancellationToken = default)
    {
        var settings = await businessSettings.GetAsync(cancellationToken)
            ?? throw new InvalidOperationException("Business settings not found.");

        var delivery = settings.Delivery;
        var taxSettings = settings.Tax;
        var currency = settings.CurrencyCode;

        // -----------------------------------------------------------------------
        // Cart — priced against live catalog data, never against stored prices.
        // -----------------------------------------------------------------------
        var cart = await db.Carts
            .AsNoTracking()
            .Include(c => c.Items)
            .FirstOrDefaultAsync(
                c => c.CustomerId == request.CustomerId && c.ExpiresAt > DateTime.UtcNow,
                cancellationToken);

        var blockingReasons = new List<string>();

        if (cart is null || cart.Items.Count == 0)
        {
            blockingReasons.Add("CART_EMPTY");
            return Result.Success(BuildUnavailableSummary(settings, currency, blockingReasons));
        }

        var productIds = cart.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await db.Products
            .AsNoTracking()
            .Where(p => productIds.Contains(p.Id) && p.Status == ProductStatus.Active)
            .Include(p => p.Images)
            .ToListAsync(cancellationToken);

        var variantIds = cart.Items.Where(i => i.VariantId.HasValue)
            .Select(i => i.VariantId!.Value).Distinct().ToList();
        var variants = variantIds.Count > 0
            ? await db.ProductVariants.AsNoTracking()
                .Where(v => variantIds.Contains(v.Id)).ToListAsync(cancellationToken)
            : [];

        var inventoryItems = await db.InventoryItems.AsNoTracking()
            .Where(inv => productIds.Contains(inv.ProductId))
            .ToListAsync(cancellationToken);

        // -----------------------------------------------------------------------
        // Line pricing
        // -----------------------------------------------------------------------
        var items = new List<CheckoutSummaryItem>(cart.Items.Count);
        var cartItemContexts = new List<CartItemContext>(cart.Items.Count);
        decimal subtotal = 0m;

        foreach (var cartItem in cart.Items)
        {
            var product = products.FirstOrDefault(p => p.Id == cartItem.ProductId);
            if (product is null)
            {
                // Archived or removed between add-to-cart and checkout. Surfaced as a blocking
                // reason rather than silently dropped, so the customer is not quoted a total
                // that excludes something they believe is in the cart.
                blockingReasons.Add($"PRODUCT_UNAVAILABLE:{cartItem.ProductId}");
                continue;
            }

            var variant = cartItem.VariantId.HasValue
                ? variants.FirstOrDefault(v => v.Id == cartItem.VariantId.Value)
                : null;

            // Inactive variants must not be purchasable even if they are still in the cart.
            if (cartItem.VariantId.HasValue && (variant is null || !variant.IsActive))
            {
                blockingReasons.Add($"VARIANT_UNAVAILABLE:{cartItem.ProductId}");
                continue;
            }

            var unitPrice = product.GetEffectivePrice(variant);
            var lineTotal = unitPrice * cartItem.Quantity;
            subtotal += lineTotal;

            var inventory = inventoryItems.FirstOrDefault(
                i => i.ProductId == cartItem.ProductId && i.VariantId == cartItem.VariantId);
            var stock = stockService.GetStockResponse(inventory);

            if (!stock.CanPurchase)
                blockingReasons.Add($"INSUFFICIENT_STOCK:{product.Id}");

            var primaryImageUrl = product.Images
                .OrderBy(i => i.SortOrder)
                .FirstOrDefault(i => i.IsPrimary)?.Asset.SecureUrl
                ?? product.Images.OrderBy(i => i.SortOrder).FirstOrDefault()?.Asset.SecureUrl;

            items.Add(new CheckoutSummaryItem(
                cartItem.Id,
                product.Id,
                variant?.Id,
                product.Name,
                product.Slug,
                variant is not null ? $"SKU: {variant.Sku ?? "N/A"}" : null,
                variant?.Sku ?? product.Sku,
                unitPrice,
                cartItem.Quantity,
                lineTotal,
                stock.Availability,
                stock.CanPurchase,
                primaryImageUrl));

            cartItemContexts.Add(new CartItemContext(product.Id, product.CategoryId, lineTotal));
        }

        if (items.Count == 0)
        {
            blockingReasons.Add("CART_EMPTY");
            return Result.Success(BuildUnavailableSummary(settings, currency, blockingReasons));
        }

        // -----------------------------------------------------------------------
        // Coupon — re-validated on every pricing pass, never trusted from the client.
        // -----------------------------------------------------------------------
        var couponCode = ResolveCouponCode(request.CouponCode, cart.CouponCode);
        var promotionResult = PromotionCalculationResult.NoPromotion();

        if (!string.IsNullOrWhiteSpace(couponCode))
        {
            var isFirstOrder = !await db.Orders
                .AnyAsync(o => o.CustomerId == request.CustomerId, cancellationToken);

            promotionResult = await promotionService.ValidateAndCalculateAsync(
                couponCode, request.CustomerId, subtotal, cartItemContexts, isFirstOrder, cancellationToken);
        }

        var discountAmount = promotionResult.IsValid ? promotionResult.DiscountAmount : 0m;

        // -----------------------------------------------------------------------
        // Shipping and COD
        //
        // COD is priced from the shipping configuration. When COD is unavailable the fee is
        // zero (DeliverySettings.EffectiveCodFee) and the method is reported unavailable, so
        // the backend never quotes a cash-on-delivery surcharge it will not honour.
        // -----------------------------------------------------------------------
        var isCod = request.PaymentMethod == PaymentMethod.CashOnDelivery;
        var codIsSelectable = request.PaymentMethod is null || isCod;

        if (isCod && !delivery.IsCodAvailable)
        {
            // Requested method is no longer offered — fall back to pricing without the COD fee
            // and report the method as unavailable.
            isCod = false;
        }

        var shipping = shippingService.Calculate(subtotal, isCod, delivery);
        var shippingAmount = shipping.ShippingAmount;
        var codFee = shipping.CodFee;

        // -----------------------------------------------------------------------
        // Tax — charged on the discounted base.
        // -----------------------------------------------------------------------
        var taxableBase = Math.Max(0m, subtotal - discountAmount);
        var tax = taxService.Calculate(taxableBase, taxSettings);
        var taxAmount = tax.TaxAmount;

        // -----------------------------------------------------------------------
        // Grand total
        //   exclusive tax: subtotal - discount + tax + shipping + codFee
        //   inclusive tax: subtotal - discount + shipping + codFee (tax already in the price)
        // -----------------------------------------------------------------------
        var grandTotal = taxSettings.IsPriceInclusive
            ? subtotal - discountAmount + shippingAmount + codFee
            : subtotal - discountAmount + taxAmount + shippingAmount + codFee;
        grandTotal = Math.Max(0m, grandTotal);

        // -----------------------------------------------------------------------
        // Payment method availability
        // -----------------------------------------------------------------------
        var razorpayConfigured = await paymentGateway.IsConfiguredAsync(cancellationToken);
        var paymentMethods = new List<CheckoutPaymentMethodAvailability>
        {
            new(PaymentMethod.Razorpay, razorpayConfigured,
                razorpayConfigured ? null : "Online payments are temporarily unavailable."),
            new(PaymentMethod.CashOnDelivery, delivery.IsCodAvailable,
                delivery.IsCodAvailable ? null : "Cash on delivery is not available for this store.")
        };
        if (request.PaymentMethod is { } requested &&
            paymentMethods.First(m => m.Method == requested) is { IsAvailable: false })
        {
            blockingReasons.Add(requested == PaymentMethod.CashOnDelivery
                ? "COD_NOT_AVAILABLE"
                : "RAZORPAY_NOT_CONFIGURED");
        }

        // -----------------------------------------------------------------------
        // Free-shipping progress
        // -----------------------------------------------------------------------
        var remainingForFreeShipping =
            !shipping.IsFreeShipping && delivery.FreeShippingThreshold.HasValue
                ? Math.Max(0m, delivery.FreeShippingThreshold.Value - subtotal)
                : 0m;

        var isReady = blockingReasons.Count == 0;

        logger.LogDebug(
            "Checkout summary for customer {CustomerId}: {ItemCount} item(s), subtotal {Subtotal}, " +
            "discount {Discount}, tax {Tax}, shipping {Shipping}, codFee {CodFee}, total {Total} {Currency}. Ready: {Ready}.",
            request.CustomerId, items.Count, subtotal, discountAmount, taxAmount,
            shippingAmount, codFee, grandTotal, currency, isReady);

        return Result.Success(new CheckoutSummary(
            items,
            subtotal,
            discountAmount,
            taxAmount,
            taxSettings.TaxLabel,
            taxSettings.IsPriceInclusive,
            shippingAmount,
            codFee,
            grandTotal,
            currency,
            promotionResult.IsValid ? promotionResult.CouponCode : couponCode,
            promotionResult.IsValid ? promotionResult.PromotionId : null,
            promotionResult.IsValid ? promotionResult.DiscountType : null,
            promotionResult.IsValid ? promotionResult.EligibleSubtotal : 0m,
            promotionResult.IsValid ? null : promotionResult.ErrorCode,
            promotionResult.IsValid ? null : promotionResult.ErrorMessage,
            shipping.IsFreeShipping,
            delivery.FreeShippingThreshold,
            remainingForFreeShipping,
            delivery.IsCodAvailable,
            shipping.DeliveryEstimate,
            razorpayConfigured,
            paymentMethods,
            isReady,
            blockingReasons.Distinct().ToList(),
            isReady));
    }

    /// <summary>
    /// The request coupon wins when supplied; otherwise the coupon stored on the cart applies.
    /// A blank request value means "do not override" rather than "remove".
    /// </summary>
    private static string? ResolveCouponCode(string? requested, string? stored) =>
        !string.IsNullOrWhiteSpace(requested) ? requested : stored;

    /// <summary>
    /// A summary for a cart that cannot be priced. Totals are zero and readiness is false,
    /// so a client can render the failure state without special-casing a missing cart.
    /// </summary>
    private static CheckoutSummary BuildUnavailableSummary(
        BusinessSettings settings, string currency, List<string> blockingReasons) =>
        new(
            Items: [],
            Subtotal: 0m,
            DiscountAmount: 0m,
            TaxAmount: 0m,
            TaxLabel: settings.Tax.TaxLabel,
            IsPriceInclusive: settings.Tax.IsPriceInclusive,
            ShippingAmount: 0m,
            CodFee: 0m,
            GrandTotal: 0m,
            CurrencyCode: currency,
            AppliedCouponCode: null,
            AppliedPromotionId: null,
            DiscountType: null,
            EligibleSubtotal: 0m,
            CouponErrorCode: null,
            CouponErrorMessage: null,
            IsFreeShipping: false,
            FreeShippingThreshold: settings.Delivery.FreeShippingThreshold,
            RemainingForFreeShipping: settings.Delivery.FreeShippingThreshold ?? 0m,
            IsCodAvailable: settings.Delivery.IsCodAvailable,
            DeliveryEstimate: null,
            IsRazorpayConfigured: false,
            PaymentMethods: [
                new(PaymentMethod.Razorpay, false, "Online payments are temporarily unavailable."),
                new(PaymentMethod.CashOnDelivery, settings.Delivery.IsCodAvailable,
                    settings.Delivery.IsCodAvailable ? null : "Cash on delivery is not available for this store.")
            ],
            IsReadyToCheckout: false,
            BlockingReasons: blockingReasons,
            IsQuotable: false);
}
