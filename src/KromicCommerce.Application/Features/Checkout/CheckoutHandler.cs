using System.Text.Json;
using KromicCommerce.Application.Abstractions.Commerce;
using KromicCommerce.Domain.Promotions;

namespace KromicCommerce.Application.Features.Checkout;

/// <summary>
/// Checkout handler — the core purchase transaction.
///
/// Server-side guarantees (enforced here, never trusted from client):
///   1. All prices come from <see cref="ICheckoutSummaryService"/> — the same engine that
///      backs GET /checkout/summary — so the amount shown at checkout and the amount charged
///      cannot diverge. The request carries no monetary value at all.
///   2. Shipping fee and COD fee from IShippingCalculationService — reads DeliverySettings.
///      COD availability and its fee both come from the shipping configuration.
///   3. Tax from ITaxCalculationService — reads BusinessSettings.Tax.
///   4. Promotion discount from IPromotionService — re-validated at order creation time.
///   5. Inventory reserved with xmin concurrency token (PostgreSQL row-level).
///   6. Promotion usage recorded at placement (COD) or after payment confirmation (Razorpay).
///   7. Order financial snapshot is immutable after creation.
///
/// Pricing is not reimplemented here. The summary is recalculated inside this handler rather
/// than reused from a previous request, so a cart, coupon or shipping configuration that
/// changed since the customer saw the summary cannot be charged a stale amount.
/// </summary>
internal sealed class CheckoutHandler(
    IApplicationDbContext db,
    IBusinessSettingsService businessSettings,
    ICheckoutSummaryService summaryService,
    IPaymentGateway paymentGateway,
    ILogger<CheckoutHandler> logger)
    : ICommandHandler<CheckoutCommand, CheckoutResponse>
{
    public async Task<Result<CheckoutResponse>> Handle(
        CheckoutCommand command, CancellationToken cancellationToken)
    {
        // -----------------------------------------------------------------------
        // Idempotency
        // -----------------------------------------------------------------------
        if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            var existing = await db.Orders
                .FirstOrDefaultAsync(
                    o => o.CustomerId == command.CustomerId
                         && EF.Property<string?>(o, "IdempotencyKey") == command.IdempotencyKey,
                    cancellationToken);
            if (existing is not null)
            {
                if (existing.Status == OrderStatus.Failed)
                    return Result.Failure<CheckoutResponse>(
                        Error.Conflict("CHECKOUT_ALREADY_FAILED",
                            "This checkout attempt failed. Start a new checkout attempt to retry payment."));

                return await BuildCheckoutResponse(existing, cancellationToken);
            }
        }

        // -----------------------------------------------------------------------
        // Saved address — validated before anything is priced, and scoped to the
        // customer so an ID from another account can never be used.
        // -----------------------------------------------------------------------
        var customerAddress = await db.CustomerAddresses
            .AsNoTracking()
            .FirstOrDefaultAsync(
                a => a.Id == command.AddressId && a.CustomerId == command.CustomerId,
                cancellationToken);

        if (customerAddress is null)
            return Result.Failure<CheckoutResponse>(
                Error.NotFound("ADDRESS_NOT_FOUND", "Address not found."));

        if (string.IsNullOrWhiteSpace(customerAddress.Phone))
            return Result.Failure<CheckoutResponse>(
                Error.Validation("ADDRESS_PHONE_REQUIRED", "The selected address must include a phone number."));

        // -----------------------------------------------------------------------
        // Authoritative pricing — one engine, recalculated here.
        // -----------------------------------------------------------------------
        var summaryResult = await summaryService.CalculateAsync(
            new CheckoutSummaryRequest(
                command.CustomerId,
                command.PaymentMethod,
                command.CouponCode),
            cancellationToken);

        if (!summaryResult.IsSuccess)
            return Result.Failure<CheckoutResponse>(summaryResult.Error);

        var summary = summaryResult.Value;

        // A coupon the customer expects to be honoured must fail the checkout rather than
        // silently charging full price. An invalid cart or an unavailable payment method
        // fails with the same error codes the endpoint has always returned.
        if (!string.IsNullOrWhiteSpace(summary.CouponErrorCode) &&
            CouponWasRequested(command.CouponCode, summary.AppliedCouponCode))
        {
            return Result.Failure<CheckoutResponse>(Error.Validation(
                summary.CouponErrorCode,
                summary.CouponErrorMessage ?? "Coupon is not valid."));
        }

        var blockingError = MapBlockingReason(summary);
        if (blockingError is not null)
            return Result.Failure<CheckoutResponse>(blockingError);

        var settings = await businessSettings.GetAsync(cancellationToken)
            ?? throw new InvalidOperationException("Business settings not found.");

        var currency = settings.CurrencyCode;
        var subtotal = summary.Subtotal;
        var discountAmount = summary.DiscountAmount;
        var taxAmount = summary.TaxAmount;
        var shippingAmount = summary.ShippingAmount;
        var codFee = summary.CodFee;
        var grandTotal = summary.GrandTotal;
        var paymentMethod = command.PaymentMethod;
        var isCod = paymentMethod == PaymentMethod.CashOnDelivery;
        var appliedCoupon = summary.AppliedCouponCode;

        // -----------------------------------------------------------------------
        // Inventory reservation
        // -----------------------------------------------------------------------
        var cart = await db.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(
                c => c.CustomerId == command.CustomerId && c.ExpiresAt > DateTime.UtcNow,
                cancellationToken);

        if (cart is null)
            return Result.Failure<CheckoutResponse>(
                Error.Validation("CART_EMPTY", "Your cart is empty."));

        var productIds = summary.Items.Select(i => i.ProductId).Distinct().ToList();
        var inventoryItems = await db.InventoryItems
            .Where(inv => productIds.Contains(inv.ProductId))
            .ToListAsync(cancellationToken);

        foreach (var line in summary.Items)
        {
            var inv = inventoryItems.FirstOrDefault(i =>
                i.ProductId == line.ProductId && i.VariantId == line.VariantId);

            if (inv is not null)
            {
                try { inv.Reserve(line.Quantity); }
                catch (InvalidOperationException ex)
                {
                    return Result.Failure<CheckoutResponse>(
                        Error.Conflict("INSUFFICIENT_INVENTORY", ex.Message));
                }
            }
        }

        // -----------------------------------------------------------------------
        // Build and persist Order
        // -----------------------------------------------------------------------
        var address = ShippingAddress.Create(
            customerAddress.FullName, customerAddress.Phone,
            customerAddress.AddressLine1, customerAddress.AddressLine2,
            customerAddress.City, customerAddress.State,
            customerAddress.PostalCode, customerAddress.CountryCode);

        var orderNumber = GenerateOrderNumber();

        var order = Order.Create(
            command.CustomerId, orderNumber, currency,
            subtotal, shippingAmount, codFee, discountAmount, taxAmount, grandTotal,
            address, paymentMethod, appliedCoupon);

        if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
            db.Orders.Entry(order).Property("IdempotencyKey").CurrentValue = command.IdempotencyKey;

        db.Orders.Add(order);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var line in summary.Items)
        {
            var oi = OrderItem.Create(
                order.Id, line.ProductId, line.VariantId,
                line.ProductName, line.VariantDescription, line.Sku,
                line.UnitPrice, line.Quantity);
            order.AddItem(oi);
            db.OrderItems.Add(oi);
        }

        var payment = Payment.Create(order.Id, paymentMethod.ToString(), grandTotal, currency);
        db.Payments.Add(payment);

        if (isCod)
        {
            // COD orders are placed immediately. Razorpay orders emit payment confirmation
            // only after a verified payment, so a failed gateway setup never sends an order email.
            var outboxPayload = JsonSerializer.Serialize(new
            {
                order.Id, order.OrderNumber, command.CustomerId,
                order.GrandTotal, order.CurrencyCode, order.Subtotal,
                order.ShippingAmount, order.DiscountAmount, order.TaxAmount,
                order.CodFee, order.AppliedCouponCode,
                PaymentMethod = order.PaymentMethod.ToString(),
                ShippingAddress = new
                {
                    order.ShippingAddress.FullName,
                    order.ShippingAddress.AddressLine1,
                    order.ShippingAddress.City,
                    order.ShippingAddress.State,
                    order.ShippingAddress.Country
                }
            });
            db.OutboxEvents.Add(OutboxEvent.Create("OrderPlaced", outboxPayload));
        }

        // COD: order stays at OrderPlaced — admin must confirm after stock verification.
        // Payment is collected on delivery; do NOT mark paid or confirm here.
        if (isCod && summary.AppliedPromotionId.HasValue)
        {
            // Record promotion usage for COD at placement time (not at confirmation)
            // — coupon is consumed when order is placed, not when admin confirms.
            await RecordPromotionUsageAsync(
                summary.AppliedPromotionId.Value, command.CustomerId, order.Id, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        // -----------------------------------------------------------------------
        // Razorpay — outside DB transaction
        // -----------------------------------------------------------------------
        string? providerOrderId = null;

        if (paymentMethod == PaymentMethod.Razorpay)
        {
            var pgResult = await paymentGateway.CreateOrderAsync(
                order.Id, grandTotal, currency, order.OrderNumber, cancellationToken);

            if (!pgResult.Success)
            {
                logger.LogError("Razorpay order creation failed for Order {OrderId}: {Error}",
                    order.Id, pgResult.ErrorMessage);
                payment.MarkFailed(pgResult.ErrorMessage);
                order.MarkFailed();
                ReleaseReservations(summary, inventoryItems);
                await db.SaveChangesAsync(cancellationToken);
                return Result.Failure<CheckoutResponse>(
                    Error.ServiceUnavailable("PAYMENT_INITIALIZATION_FAILED",
                        "Online payment could not be initialized. Please try again later."));
            }
            else
            {
                providerOrderId = pgResult.ProviderOrderId;
                payment.SetProviderOrderId(providerOrderId!);
                // Transition OrderPlaced → PendingPayment now that payment widget can be shown
                order.MarkPendingPayment();
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        // Clear cart
        cart.Clear();
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Checkout completed. OrderId: {OrderId} Subtotal: {Subtotal} Discount: {Discount} " +
            "Tax: {Tax} Shipping: {Shipping} CodFee: {CodFee} Total: {Total} {Currency}",
            order.Id, subtotal, discountAmount, taxAmount, shippingAmount, codFee, grandTotal, currency);

        return Result.Success(new CheckoutResponse(
            order.Id, order.OrderNumber, order.Status,
            order.PaymentMethod,
            subtotal, shippingAmount, codFee, discountAmount, taxAmount,
            grandTotal, currency,
            appliedCoupon, providerOrderId,
            null, // RazorpayKeyId — injected by controller
            order.CreatedAtUtc));
    }

    // -----------------------------------------------------------------------
    // Blocking-reason mapping
    //
    // The summary service reports readiness as machine-readable reasons so it stays
    // reusable. This maps them back onto the error codes this endpoint has always
    // returned, keeping the public API contract stable.
    // -----------------------------------------------------------------------

    private static Error? MapBlockingReason(CheckoutSummary summary)
    {
        foreach (var reason in summary.BlockingReasons)
        {
            if (reason == "CART_EMPTY")
                return Error.Validation("CART_EMPTY", "Your cart is empty.");

            if (reason == "COD_NOT_AVAILABLE")
                return Error.Validation("COD_NOT_AVAILABLE",
                    "Cash on delivery is not available for this store.");

            if (reason == "RAZORPAY_NOT_CONFIGURED")
                return Error.ServiceUnavailable("RAZORPAY_NOT_CONFIGURED",
                    "Online payments are temporarily unavailable. Please choose another payment method or try again later.");

            if (reason.StartsWith("PRODUCT_UNAVAILABLE:", StringComparison.Ordinal))
                return Error.NotFound("PRODUCT_UNAVAILABLE",
                    $"Product {reason["PRODUCT_UNAVAILABLE:".Length..]} is no longer available.");

            if (reason.StartsWith("VARIANT_UNAVAILABLE:", StringComparison.Ordinal))
                return Error.NotFound("PRODUCT_UNAVAILABLE",
                    $"Product {reason["VARIANT_UNAVAILABLE:".Length..]} is no longer available in the selected option.");

            if (reason.StartsWith("INSUFFICIENT_STOCK:", StringComparison.Ordinal))
                return Error.Conflict("INSUFFICIENT_INVENTORY",
                    "One or more items no longer have enough stock to complete this order.");
        }

        return null;
    }

    /// <summary>
    /// Whether a coupon was expected. An explicit request code or a code already stored on
    /// the cart both count, so an applied coupon that has since become ineligible fails
    /// checkout instead of being silently dropped.
    /// </summary>
    private static bool CouponWasRequested(string? requested, string? applied) =>
        !string.IsNullOrWhiteSpace(requested) || !string.IsNullOrWhiteSpace(applied);

    // -----------------------------------------------------------------------
    // Promotion usage recording — concurrency-safe via xmin token
    //
    // Strategy: optimistic concurrency using PostgreSQL's xmin system column.
    // Each attempt reads the Promotion row fresh, checks the limit, increments,
    // and tries to save. If xmin changed (another checkout snuck in), EF raises
    // DbUpdateConcurrencyException. We reload and retry up to maxRetries times.
    // The PromotionUsage insert is bundled in the same SaveChangesAsync call,
    // so either both succeed or both fail — no partial state.
    // -----------------------------------------------------------------------

    private async Task RecordPromotionUsageAsync(
        Guid promotionId, Guid customerId, Guid orderId, CancellationToken ct)
    {
        const int maxRetries = 3;
        PromotionUsage? usageToAdd = null;

        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            var promotion = await db.Promotions
                .FirstOrDefaultAsync(p => p.Id == promotionId, ct);
            if (promotion is null) return;

            if (!promotion.HasRemainingUsage())
                throw new InvalidOperationException(
                    "This coupon has reached its usage limit.");

            promotion.IncrementUsage();

            // Create a fresh usage entity on each attempt; only one will ever be saved
            usageToAdd = PromotionUsage.Create(promotionId, customerId, orderId);
            db.PromotionUsages.Add(usageToAdd);

            try
            {
                await db.SaveChangesAsync(ct);
                return; // both the increment and the usage row committed
            }
            catch (DbUpdateConcurrencyException ex) when (attempt < maxRetries - 1)
            {
                // Reload the stale Promotion entry so next iteration reads fresh xmin
                foreach (var entry in ex.Entries)
                    await entry.ReloadAsync(ct);

                // Remove the un-saved usage row from the change tracker
                db.PromotionUsages.Remove(usageToAdd);
            }
        }

        throw new InvalidOperationException(
            "Could not reserve coupon usage after multiple attempts. Please try again.");
    }

    private static void ReleaseReservations(
        CheckoutSummary summary,
        IReadOnlyList<Domain.Catalog.InventoryItem> inventoryItems)
    {
        foreach (var line in summary.Items)
        {
            var inventory = inventoryItems.FirstOrDefault(i =>
                i.ProductId == line.ProductId && i.VariantId == line.VariantId);
            inventory?.Release(line.Quantity);
        }
    }

    // -----------------------------------------------------------------------
    // Idempotency return path
    // -----------------------------------------------------------------------

    private async Task<Result<CheckoutResponse>> BuildCheckoutResponse(
        Order order, CancellationToken ct)
    {
        var payment = await db.Payments.FirstOrDefaultAsync(p => p.OrderId == order.Id, ct);
        return Result.Success(new CheckoutResponse(
            order.Id, order.OrderNumber, order.Status,
            order.PaymentMethod,
            order.Subtotal, order.ShippingAmount, order.CodFee,
            order.DiscountAmount, order.TaxAmount, order.GrandTotal,
            order.CurrencyCode,
            order.AppliedCouponCode,
            payment?.ProviderOrderId,
            null,
            order.CreatedAtUtc));
    }

    private static string GenerateOrderNumber() =>
        $"ORD-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
}
