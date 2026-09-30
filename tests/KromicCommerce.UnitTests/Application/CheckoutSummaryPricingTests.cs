using System.Linq.Expressions;
using KromicCommerce.Application.Abstractions.Payments;
using KromicCommerce.Application.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace KromicCommerce.UnitTests.Application;

/// <summary>
/// Authoritative checkout pricing.
///
/// The backend is the source of truth: the summary endpoint and the checkout command share one
/// service, so the total shown to a customer and the total charged are produced by the same
/// code. These tests pin the arithmetic and the rules that decide whether an order may be
/// placed at all — the cases a client cannot be trusted to enforce for itself.
/// </summary>
public sealed class CheckoutSummaryPricingTests
{
    private readonly Mock<IApplicationDbContext> _db = new();
    private readonly Mock<IBusinessSettingsService> _settings = new();
    private readonly Mock<IPromotionService> _promotions = new();
    private readonly Mock<IShippingCalculationService> _shipping = new();
    private readonly Mock<ITaxCalculationService> _tax = new();
    private readonly Mock<IStorefrontStockService> _stock = new();
    private readonly Mock<IPaymentGateway> _gateway = new();

    private const string CaseInsensitiveMessage = "checkout summary must be quotable";

    // -----------------------------------------------------------------------
    // Line pricing comes from the catalog, never from the cart
    // -----------------------------------------------------------------------

    /// <summary>
    /// Cart items carry no price. The summary reads live catalog prices, so a price change
    /// between add-to-cart and checkout is reflected immediately rather than being honoured
    /// from whatever the customer last saw.
    /// </summary>
    [Fact]
    public async Task Line_prices_are_taken_from_the_live_catalog()
    {
        var product = BuildProduct(price: 120m);
        var customerId = Guid.NewGuid();
        var cart = BuildCart(customerId, (product.Id, null, 2));
        SetupCart(cart);
        SetupProducts([product]);
        SetupVariants([]);
        SetupInventory([]);
        SetupSettings(codEnabled: false);
        SetupShipping(flat: 0m);
        SetupTax(amount: 0m);

        var result = await BuildServiceWithDefaults().CalculateAsync(Request(customerId));

        result.IsSuccess.Should().BeTrue();
        result.Value.Subtotal.Should().Be(240m);
        result.Value.Items.Single().UnitPrice.Should().Be(120m);
    }

    /// <summary>
    /// A variant's price override wins over the base product price. Getting this wrong would
    /// charge a premium variant at the base price.
    /// </summary>
    [Fact]
    public async Task A_variants_price_override_replaces_the_base_price()
    {
        var product = BuildProduct(price: 100m);
        var customerId = Guid.NewGuid();
        var variant = BuildVariant(product.Id, priceOverride: 250m);
        var cart = BuildCart(customerId, (product.Id, variant.Id, 1));
        SetupCart(cart);
        SetupProducts([product]);
        SetupVariants([variant]);
        SetupInventory([]);
        SetupSettings(codEnabled: false);
        SetupShipping(flat: 0m);
        SetupTax(amount: 0m);

        var result = await BuildServiceWithDefaults().CalculateAsync(Request(customerId));

        result.Value.Subtotal.Should().Be(250m);
    }

    // -----------------------------------------------------------------------
    // Availability blocks the order
    // -----------------------------------------------------------------------

    /// <summary>
    /// A product archived after it was added to the cart must block checkout rather than be
    /// silently dropped — the customer would otherwise be quoted a total for an order that no
    /// longer contains what they selected.
    /// </summary>
    [Fact]
    public async Task An_unavailable_product_blocks_checkout_with_a_reason()
    {
        var product = BuildProduct(price: 100m);
        var customerId = Guid.NewGuid();
        var cart = BuildCart(customerId, (product.Id, null, 1));
        SetupCart(cart);
        SetupProducts([]);   // the product is gone / no longer Active
        SetupVariants([]);
        SetupInventory([]);
        SetupSettings(codEnabled: false);

        var result = await BuildServiceWithDefaults().CalculateAsync(Request(customerId));

        result.Value.IsReadyToCheckout.Should().BeFalse();
        result.Value.IsQuotable.Should().BeFalse();
        result.Value.BlockingReasons.Should().Contain(r => r.StartsWith("PRODUCT_UNAVAILABLE:"));
    }

    /// <summary>
    /// An inactive variant is not purchasable even though the product itself is fine, so the
    /// line must be blocked rather than priced at the base price.
    /// </summary>
    [Fact]
    public async Task An_inactive_variant_blocks_checkout()
    {
        var product = BuildProduct(price: 100m);
        var customerId = Guid.NewGuid();
        var variant = BuildVariant(product.Id, priceOverride: 250m);
        variant.Deactivate();
        var cart = BuildCart(customerId, (product.Id, variant.Id, 1));
        SetupCart(cart);
        SetupProducts([product]);
        SetupVariants([variant]);
        SetupInventory([]);
        SetupSettings(codEnabled: false);

        var result = await BuildServiceWithDefaults().CalculateAsync(Request(customerId));

        result.Value.IsReadyToCheckout.Should().BeFalse();
        result.Value.BlockingReasons.Should().Contain(r => r.StartsWith("VARIANT_UNAVAILABLE:"));
    }

    [Fact]
    public async Task Insufficient_stock_blocks_checkout()
    {
        var product = BuildProduct(price: 100m);
        var customerId = Guid.NewGuid();
        var cart = BuildCart(customerId, (product.Id, null, 5));
        SetupCart(cart);
        SetupProducts([product]);
        SetupVariants([]);
        SetupInventory([]);
        SetupSettings(codEnabled: false);
        SetupShipping(flat: 0m);
        SetupTax(amount: 0m);
        SetupStock(canPurchase: false);
        SetupGatewayConfigured(configured: true);

        // Builds the service directly: this test supplies its own stock arrangement, which the
        // defaults builder would otherwise overwrite.
        var result = await BuildService().CalculateAsync(Request(customerId));

        result.Value.IsReadyToCheckout.Should().BeFalse();
        result.Value.BlockingReasons.Should().Contain(r => r.StartsWith("INSUFFICIENT_STOCK:"));
        result.Value.HasStockIssues.Should().BeTrue();
    }

    [Fact]
    public async Task An_empty_cart_is_reported_as_not_ready()
    {
        var cart = BuildCart();
        SetupCart(cart);
        SetupSettings(codEnabled: false);

        var result = await BuildServiceWithDefaults().CalculateAsync(Request(Guid.NewGuid()));

        result.Value.IsReadyToCheckout.Should().BeFalse();
        result.Value.IsQuotable.Should().BeFalse();
        result.Value.BlockingReasons.Should().Contain("CART_EMPTY");
        result.Value.GrandTotal.Should().Be(0m);
    }

    /// <summary>
    /// A missing cart must not throw. The checkout screen renders the same shape for "no cart"
    /// and "cannot checkout", so it needs a zeroed summary with a reason rather than an error.
    /// </summary>
    [Fact]
    public async Task A_missing_cart_returns_a_zeroed_summary_rather_than_failing()
    {
        SetupCart(null);
        SetupSettings(codEnabled: false);

        var result = await BuildServiceWithDefaults().CalculateAsync(Request(Guid.NewGuid()));

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().BeEmpty();
        result.Value.GrandTotal.Should().Be(0m);
        result.Value.BlockingReasons.Should().Contain("CART_EMPTY");
    }

    // -----------------------------------------------------------------------
    // COD availability and fee
    // -----------------------------------------------------------------------

    /// <summary>
    /// The backend never quotes a COD surcharge for a store that has disabled COD. Trusting a
    /// client that still offers the method would produce a total the order cannot honour.
    /// </summary>
    [Fact]
    public async Task Requesting_COD_when_it_is_disabled_never_produces_a_surcharge()
    {
        var product = BuildProduct(price: 100m);
        var customerId = Guid.NewGuid();
        var cart = BuildCart(customerId, (product.Id, null, 1));
        SetupCart(cart);
        SetupProducts([product]);
        SetupVariants([]);
        SetupInventory([]);
        SetupSettings(codEnabled: false, codFee: 50m);
        SetupShipping(flat: 0m, codFee: 0m);   // EffectiveCodFee is 0 while COD is off
        SetupTax(amount: 0m);

        var result = await BuildServiceWithDefaults().CalculateAsync(
            Request(customerId, PaymentMethod.CashOnDelivery));

        result.Value.CodFee.Should().Be(0m);
        result.Value.IsCodAvailable.Should().BeFalse();
        result.Value.BlockingReasons.Should().Contain("COD_NOT_AVAILABLE");
    }

    [Fact]
    public async Task An_enabled_COD_method_reports_its_surcharge()
    {
        var product = BuildProduct(price: 100m);
        var customerId = Guid.NewGuid();
        var cart = BuildCart(customerId, (product.Id, null, 1));
        SetupCart(cart);
        SetupProducts([product]);
        SetupVariants([]);
        SetupInventory([]);
        SetupSettings(codEnabled: true, codFee: 50m);
        SetupShipping(flat: 0m, codFee: 50m);
        SetupTax(amount: 0m);

        var result = await BuildServiceWithDefaults().CalculateAsync(
            Request(customerId, PaymentMethod.CashOnDelivery));

        result.Value.CodFee.Should().Be(50m);
        result.Value.IsCodAvailable.Should().BeTrue();
        result.Value.IsReadyToCheckout.Should().BeTrue();
    }

    /// <summary>
    /// With no method chosen yet, no COD fee may be included — the customer has not asked for
    /// it, so quoting it would inflate the total they are shown.
    /// </summary>
    [Fact]
    public async Task No_COD_fee_is_quoted_before_a_payment_method_is_chosen()
    {
        var product = BuildProduct(price: 100m);
        var customerId = Guid.NewGuid();
        var cart = BuildCart(customerId, (product.Id, null, 1));
        SetupCart(cart);
        SetupProducts([product]);
        SetupVariants([]);
        SetupInventory([]);
        SetupSettings(codEnabled: true, codFee: 50m);
        SetupShipping(flat: 0m, codFee: 0m);
        SetupTax(amount: 0m);

        var result = await BuildServiceWithDefaults().CalculateAsync(Request(customerId, paymentMethod: null));

        result.Value.CodFee.Should().Be(0m);
        result.Value.IsCodAvailable.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Coupon re-validation
    // -----------------------------------------------------------------------

    /// <summary>
    /// A coupon stored on the cart is re-validated on every pass. Usage recorded elsewhere
    /// (a concurrent order) must therefore stop the discount rather than being carried forward.
    /// </summary>
    /// <summary>
    /// A coupon stored on the cart is re-validated on every pass. Usage recorded elsewhere
    /// (a concurrent order) must therefore stop the discount rather than being carried forward.
    /// </summary>
    [Fact]
    public async Task An_invalid_coupon_stops_discounting_but_still_returns_a_summary()
    {
        var product = BuildProduct(price: 100m);
        var customerId = Guid.NewGuid();
        var cart = BuildCart(customerId, (product.Id, null, 1));
        cart.ApplyCoupon("EXPIRED10");
        SetupCart(cart);
        SetupProducts([product]);
        SetupVariants([]);
        SetupInventory([]);
        SetupOrders([]);
        SetupSettings(codEnabled: false);
        SetupShipping(flat: 0m);
        SetupTax(amount: 0m);
        SetupPromotion(PromotionCalculationResult.Invalid("COUPON_EXPIRED", "This coupon has expired."));

        var result = await BuildServiceWithDefaults().CalculateAsync(Request(customerId));

        // Not a hard failure — the client can still render the cart and explain the coupon.
        result.IsSuccess.Should().BeTrue();
        result.Value.DiscountAmount.Should().Be(0m);
        result.Value.CouponErrorCode.Should().Be("COUPON_EXPIRED");
        result.Value.CouponErrorMessage.Should().Be("This coupon has expired.");
    }

    /// <summary>
    /// IMPORTANT CLIENT CONTRACT: when a coupon is rejected, AppliedCouponCode still echoes the
    /// code that was REQUESTED, not one that was granted. This is deliberate — CheckoutHandler
    /// reads it to decide whether the customer expected a discount, and must fail the checkout
    /// rather than silently charging full price (CheckoutHandler.cs:94).
    ///
    /// A client must therefore decide whether a coupon is active from
    /// <c>CouponErrorCode == null</c>, never from AppliedCouponCode being non-null. Rendering
    /// "Coupon EXPIRED10 applied" from this field while the discount is zero is the bug this
    /// test exists to prevent.
    /// </summary>
    [Fact]
    public async Task A_rejected_coupon_still_echoes_the_requested_code_for_the_checkout_guard()
    {
        var product = BuildProduct(price: 100m);
        var customerId = Guid.NewGuid();
        var cart = BuildCart(customerId, (product.Id, null, 1));
        cart.ApplyCoupon("EXPIRED10");
        SetupCart(cart);
        SetupProducts([product]);
        SetupVariants([]);
        SetupInventory([]);
        SetupOrders([]);
        SetupSettings(codEnabled: false);
        SetupShipping(flat: 0m);
        SetupTax(amount: 0m);
        SetupPromotion(PromotionCalculationResult.Invalid("COUPON_EXPIRED", "This coupon has expired."));

        var result = await BuildServiceWithDefaults().CalculateAsync(Request(customerId));

        // The code is echoed so the checkout guard can see a coupon was expected...
        result.Value.AppliedCouponCode.Should().Be("EXPIRED10");

        // ...but CouponErrorCode is the authoritative signal that it was NOT applied.
        result.Value.CouponErrorCode.Should().Be("COUPON_EXPIRED");
        result.Value.DiscountAmount.Should().Be(0m);
    }

    [Fact]
    public async Task A_valid_coupon_discounts_the_summary()
    {
        var product = BuildProduct(price: 100m);
        var customerId = Guid.NewGuid();
        var cart = BuildCart(customerId, (product.Id, null, 1));
        cart.ApplyCoupon("SAVE10");
        SetupCart(cart);
        SetupProducts([product]);
        SetupVariants([]);
        SetupInventory([]);
        SetupOrders([]);
        SetupSettings(codEnabled: false);
        SetupShipping(flat: 0m);
        SetupTax(amount: 0m);
        SetupPromotion(PromotionCalculationResult.Success(
            "SAVE10", Guid.NewGuid(), 10m, DiscountType.Percentage, 100m));

        var result = await BuildServiceWithDefaults().CalculateAsync(Request(customerId));

        result.Value.DiscountAmount.Should().Be(10m);
        result.Value.AppliedCouponCode.Should().Be("SAVE10");
        result.Value.CouponErrorCode.Should().BeNull();
    }

    /// <summary>
    /// An explicit code in the request wins over the one on the cart, which is what lets a
    /// client preview a code before committing it to the cart.
    /// </summary>
    [Fact]
    public async Task A_request_coupon_overrides_the_coupon_stored_on_the_cart()
    {
        var product = BuildProduct(price: 100m);
        var customerId = Guid.NewGuid();
        var cart = BuildCart(customerId, (product.Id, null, 1));
        cart.ApplyCoupon("SAVED");
        SetupCart(cart);
        SetupProducts([product]);
        SetupVariants([]);
        SetupInventory([]);
        SetupOrders([]);
        SetupSettings(codEnabled: false);
        SetupShipping(flat: 0m);
        SetupTax(amount: 0m);
        SetupPromotion(PromotionCalculationResult.Success(
            "PREVIEW", Guid.NewGuid(), 5m, DiscountType.FixedAmount, 100m));

        var result = await BuildServiceWithDefaults().CalculateAsync(
            Request(customerId, couponCode: "PREVIEW"));

        result.Value.AppliedCouponCode.Should().Be("PREVIEW");
    }

    /// <summary>
    /// A blank request value means "no override", not "remove the coupon". Treating "" as a
    /// removal would silently drop a valid cart coupon.
    /// </summary>
    [Fact]
    public async Task A_blank_request_coupon_does_not_override_the_cart_coupon()
    {
        var product = BuildProduct(price: 100m);
        var customerId = Guid.NewGuid();
        var cart = BuildCart(customerId, (product.Id, null, 1));
        cart.ApplyCoupon("SAVED");
        SetupCart(cart);
        SetupProducts([product]);
        SetupVariants([]);
        SetupInventory([]);
        SetupOrders([]);
        SetupSettings(codEnabled: false);
        SetupShipping(flat: 0m);
        SetupTax(amount: 0m);
        SetupPromotion(PromotionCalculationResult.Success(
            "SAVED", Guid.NewGuid(), 20m, DiscountType.FixedAmount, 100m));

        var result = await BuildServiceWithDefaults().CalculateAsync(Request(customerId, couponCode: "   "));

        result.Value.AppliedCouponCode.Should().Be("SAVED");
        result.Value.DiscountAmount.Should().Be(20m);
    }

    // -----------------------------------------------------------------------
    // Totals
    // -----------------------------------------------------------------------

    [Fact]
    public async Task The_grand_total_is_subtotal_minus_discount_plus_tax_and_shipping()
    {
        var product = BuildProduct(price: 100m);
        var customerId = Guid.NewGuid();
        var cart = BuildCart(customerId, (product.Id, null, 1));
        cart.ApplyCoupon("SAVE10");
        SetupCart(cart);
        SetupProducts([product]);
        SetupVariants([]);
        SetupInventory([]);
        SetupOrders([]);
        SetupSettings(codEnabled: true, codFee: 25m, taxPercentage: 18m);
        SetupShipping(flat: 50m, codFee: 0m);
        SetupTax(amount: 16.20m);   // 18% of the discounted base
        SetupPromotion(PromotionCalculationResult.Success(
            "SAVE10", Guid.NewGuid(), 10m, DiscountType.FixedAmount, 100m));

        var result = await BuildServiceWithDefaults().CalculateAsync(Request(customerId));

        result.Value.Subtotal.Should().Be(100m);
        result.Value.DiscountAmount.Should().Be(10m);
        result.Value.ShippingAmount.Should().Be(50m);
        // 100 - 10 + 16.20 + 50 + 0
        result.Value.GrandTotal.Should().Be(156.20m);
    }

    /// <summary>
    /// Tax is computed on the discounted base. Applying it to the pre-discount subtotal would
    /// overcharge the customer by tax on the amount they were given off.
    /// </summary>
    [Fact]
    public async Task Tax_is_charged_on_the_discounted_base()
    {
        var product = BuildProduct(price: 200m);
        var customerId = Guid.NewGuid();
        var cart = BuildCart(customerId, (product.Id, null, 1));
        cart.ApplyCoupon("HALF");
        SetupCart(cart);
        SetupProducts([product]);
        SetupVariants([]);
        SetupInventory([]);
        SetupOrders([]);
        SetupSettings(codEnabled: false, taxPercentage: 10m);
        SetupShipping(flat: 0m);
        SetupTax(amount: 10m);   // 10% of 100, not of 200
        SetupPromotion(PromotionCalculationResult.Success(
            "HALF", Guid.NewGuid(), 100m, DiscountType.FixedAmount, 200m));

        var result = await BuildServiceWithDefaults().CalculateAsync(Request(customerId));

        // 200 - 100 + 10 + 0
        result.Value.GrandTotal.Should().Be(110m);
    }

    /// <summary>
    /// With price-inclusive tax the tax is already inside the line prices, so adding it again
    /// would double-charge. The total is therefore subtotal + shipping only.
    /// </summary>
    [Fact]
    public async Task Price_inclusive_tax_is_not_added_again_to_the_total()
    {
        var product = BuildProduct(price: 100m);
        var customerId = Guid.NewGuid();
        var cart = BuildCart(customerId, (product.Id, null, 1));
        SetupCart(cart);
        SetupProducts([product]);
        SetupVariants([]);
        SetupInventory([]);
        SetupSettings(codEnabled: false, taxPercentage: 18m, isPriceInclusive: true);
        SetupShipping(flat: 40m);
        SetupTax(amount: 18m);

        var result = await BuildServiceWithDefaults().CalculateAsync(Request(customerId));

        result.Value.IsPriceInclusive.Should().BeTrue();
        // 100 + 40, with the 18 already inside the 100.
        result.Value.GrandTotal.Should().Be(140m);
    }

    /// <summary>
    /// A discount larger than the cart cannot drive the payable amount negative.
    /// </summary>
    [Fact]
    public async Task The_grand_total_is_never_negative()
    {
        var product = BuildProduct(price: 100m);
        var customerId = Guid.NewGuid();
        var cart = BuildCart(customerId, (product.Id, null, 1));
        cart.ApplyCoupon("HUGE");
        SetupCart(cart);
        SetupProducts([product]);
        SetupVariants([]);
        SetupInventory([]);
        SetupOrders([]);
        SetupSettings(codEnabled: false, taxPercentage: 5m);
        SetupShipping(flat: 0m);
        SetupTax(amount: 0m);
        SetupPromotion(PromotionCalculationResult.Success(
            "HUGE", Guid.NewGuid(), 500m, DiscountType.FixedAmount, 50m));

        var result = await BuildServiceWithDefaults().CalculateAsync(Request(customerId));

        result.Value.GrandTotal.Should().Be(0m);
    }

    // -----------------------------------------------------------------------
    // Payment method availability
    // -----------------------------------------------------------------------

    [Fact]
    public async Task An_unconfigured_payment_gateway_blocks_an_online_order()
    {
        var product = BuildProduct(price: 100m);
        var customerId = Guid.NewGuid();
        var cart = BuildCart(customerId, (product.Id, null, 1));
        SetupCart(cart);
        SetupProducts([product]);
        SetupVariants([]);
        SetupInventory([]);
        SetupSettings(codEnabled: false);
        SetupShipping(flat: 0m);
        SetupTax(amount: 0m);
        SetupStock(canPurchase: true);
        SetupGatewayConfigured(configured: false);

        // Builds the service directly: this test supplies its own gateway arrangement, which
        // the defaults builder would otherwise overwrite.
        var result = await BuildService().CalculateAsync(
            Request(customerId, PaymentMethod.Razorpay));

        result.Value.IsRazorpayConfigured.Should().BeFalse();
        result.Value.IsReadyToCheckout.Should().BeFalse();
        result.Value.BlockingReasons.Should().Contain("RAZORPAY_NOT_CONFIGURED");
    }

    [Fact]
    public async Task Both_payment_methods_are_reported_with_availability_and_reason()
    {
        var product = BuildProduct(price: 100m);
        var customerId = Guid.NewGuid();
        var cart = BuildCart(customerId, (product.Id, null, 1));
        SetupCart(cart);
        SetupProducts([product]);
        SetupVariants([]);
        SetupInventory([]);
        SetupSettings(codEnabled: false);
        SetupShipping(flat: 0m);
        SetupTax(amount: 0m);
        SetupGatewayConfigured(true);

        var result = await BuildServiceWithDefaults().CalculateAsync(Request(customerId));

        result.Value.PaymentMethods.Should().HaveCount(2);
        result.Value.PaymentMethods.Single(m => m.Method == PaymentMethod.Razorpay)
            .IsAvailable.Should().BeTrue();
        var cod = result.Value.PaymentMethods.Single(m => m.Method == PaymentMethod.CashOnDelivery);
        cod.IsAvailable.Should().BeFalse();
        cod.UnavailableReason.Should().NotBeNullOrWhiteSpace();
    }

    // -----------------------------------------------------------------------
    // Free shipping progress
    // -----------------------------------------------------------------------

    [Fact]
    public async Task The_remaining_amount_for_free_shipping_is_reported()
    {
        var product = BuildProduct(price: 200m);
        var customerId = Guid.NewGuid();
        var cart = BuildCart(customerId, (product.Id, null, 1));
        SetupCart(cart);
        SetupProducts([product]);
        SetupVariants([]);
        SetupInventory([]);
        SetupSettings(codEnabled: false, freeShippingThreshold: 500m);
        SetupShipping(flat: 50m, isFreeShipping: false);
        SetupTax(amount: 0m);

        var result = await BuildServiceWithDefaults().CalculateAsync(Request(customerId));

        result.Value.IsFreeShipping.Should().BeFalse();
        result.Value.FreeShippingThreshold.Should().Be(500m);
        result.Value.RemainingForFreeShipping.Should().Be(300m);
    }

    [Fact]
    public async Task Nothing_remains_to_be_spent_once_free_shipping_applies()
    {
        var product = BuildProduct(price: 600m);
        var customerId = Guid.NewGuid();
        var cart = BuildCart(customerId, (product.Id, null, 1));
        SetupCart(cart);
        SetupProducts([product]);
        SetupVariants([]);
        SetupInventory([]);
        SetupSettings(codEnabled: false, freeShippingThreshold: 500m);
        SetupShipping(flat: 0m, isFreeShipping: true);
        SetupTax(amount: 0m);

        var result = await BuildServiceWithDefaults().CalculateAsync(Request(customerId));

        result.Value.IsFreeShipping.Should().BeTrue();
        result.Value.RemainingForFreeShipping.Should().Be(0m);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private CheckoutSummaryService BuildService()
        => new(_db.Object, _settings.Object, _promotions.Object, _shipping.Object,
               _tax.Object, _stock.Object, _gateway.Object,
               NullLogger<CheckoutSummaryService>.Instance);

    /// <summary>
    /// Every test needs a stock response and a gateway verdict, because the service reads both
    /// for every quote and a strict mock returns null otherwise. These defaults represent a
    /// healthy store: stock available, Razorpay configured.
    ///
    /// A test that needs a different outcome must build the service itself (or call
    /// SetupStock / SetupGatewayConfigured) rather than using this builder, so its own
    /// arrangement is not silently overwritten by these defaults.
    /// </summary>
    private CheckoutSummaryService BuildServiceWithDefaults()
    {
        SetupStock(canPurchase: true);
        SetupGatewayConfigured(configured: true);
        return BuildService();
    }

    private static CheckoutSummaryRequest Request(
        Guid customerId, PaymentMethod? paymentMethod = null, string? couponCode = null)
        => new(customerId, paymentMethod, couponCode);

    private static Product BuildProduct(decimal price)
    {
        var product = Product.Create("Widget", "widget", "SKU-1", price, null, null);
        typeof(KromicCommerce.Domain.Common.Entity)
            .GetProperty("Id")!.SetValue(product, Guid.NewGuid());
        product.Publish();
        return product;
    }

    private static ProductVariant BuildVariant(Guid productId, decimal? priceOverride)
    {
        var variant = ProductVariant.Create(productId, "SKU-1-L", priceOverride);
        typeof(KromicCommerce.Domain.Common.Entity)
            .GetProperty("Id")!.SetValue(variant, Guid.NewGuid());
        return variant;
    }

    /// <summary>
    /// Builds a cart for a customer. CartItem.Create is internal to the Domain assembly, so
    /// items are added through the aggregate, which is the same path production uses.
    /// </summary>
    private static Cart BuildCart(Guid customerId, params (Guid ProductId, Guid? VariantId, int Quantity)[] lines)
    {
        var cart = Cart.CreateForCustomer(customerId);
        foreach (var (productId, variantId, quantity) in lines)
            cart.AddItem(productId, variantId, quantity);
        return cart;
    }

    private static Cart BuildCart() => BuildCart(Guid.NewGuid());

    private void SetupSettings(
        bool codEnabled,
        decimal codFee = 0m,
        decimal? freeShippingThreshold = null,
        decimal taxPercentage = 0m,
        bool isPriceInclusive = false)
    {
        var settings = BusinessSettings.CreateDefault("My Store");
        settings.UpdateDelivery(DeliverySettings.Create(
            flatFeeAmount: 50m,
            freeShippingThreshold: freeShippingThreshold,
            codEnabled: codEnabled,
            codExtraFee: codFee,
            processingDays: 1,
            minDeliveryDays: 3,
            maxDeliveryDays: 7));
        settings.UpdateTax(TaxSettings.Create(
            taxEnabled: taxPercentage > 0m,
            taxPercentage: taxPercentage > 0m ? taxPercentage : 5m,
            isPriceInclusive: isPriceInclusive,
            taxLabel: "GST"));

        _settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(settings);
    }

    private void SetupShipping(decimal flat, decimal codFee = 0m, bool isFreeShipping = false)
        => _shipping.Setup(s => s.Calculate(It.IsAny<decimal>(), It.IsAny<bool>(), It.IsAny<DeliverySettings>()))
            .Returns(new ShippingCalculationResult(flat, isFreeShipping, codFee, null));

    private void SetupTax(decimal amount)
        => _tax.Setup(t => t.Calculate(It.IsAny<decimal>(), It.IsAny<TaxSettings>()))
            .Returns(new TaxCalculationResult(amount, 18m, false, "GST"));

    private void SetupStock(bool canPurchase)
        => _stock.Setup(s => s.GetStockResponse(It.IsAny<InventoryItem?>()))
            .Returns(new PublicStockResponse(
                canPurchase ? StockAvailability.InStock : StockAvailability.OutOfStock,
                canPurchase));

    private void SetupPromotion(PromotionCalculationResult result)
        => _promotions.Setup(p => p.ValidateAndCalculateAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<decimal>(),
                It.IsAny<IReadOnlyList<CartItemContext>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    private void SetupGatewayConfigured(bool configured)
        => _gateway.Setup(g => g.IsConfiguredAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(configured);

    private void SetupCart(Cart? cart)
        => SetupDbSet(d => d.Carts, cart is null ? [] : [cart]);

    private void SetupProducts(List<Product> products)
        => SetupDbSet(d => d.Products, products);

    private void SetupVariants(List<ProductVariant> variants)
        => SetupDbSet(d => d.ProductVariants, variants);

    private void SetupInventory(List<InventoryItem> items)
        => SetupDbSet(d => d.InventoryItems, items);

    private void SetupOrders(List<Order> orders)
        => SetupDbSet(d => d.Orders, orders);

    private void SetupDbSet<T>(
        Expression<Func<IApplicationDbContext, DbSet<T>>> selector, List<T> data)
        where T : class
    {
        var queryable = data.AsQueryable();
        var mock = new Mock<DbSet<T>>();
        mock.As<IAsyncEnumerable<T>>()
            .Setup(m => m.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns(new TestAsyncEnumerator<T>(queryable.GetEnumerator()));
        mock.As<IQueryable<T>>().Setup(m => m.Provider)
            .Returns(new TestAsyncQueryProvider<T>(queryable.Provider));
        mock.As<IQueryable<T>>().Setup(m => m.Expression).Returns(queryable.Expression);
        mock.As<IQueryable<T>>().Setup(m => m.ElementType).Returns(queryable.ElementType);
        mock.As<IQueryable<T>>().Setup(m => m.GetEnumerator()).Returns(queryable.GetEnumerator());
        _db.Setup(selector).Returns(mock.Object);
    }
}
