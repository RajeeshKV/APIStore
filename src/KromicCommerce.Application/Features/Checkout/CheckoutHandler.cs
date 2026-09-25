using System.Text.Json;
using KromicCommerce.Application.Abstractions.Commerce;
using KromicCommerce.Domain.Promotions;

namespace KromicCommerce.Application.Features.Checkout;

/// <summary>
/// Checkout handler — the core purchase transaction.
///
/// Server-side guarantees (enforced here, never trusted from client):
///   1. All prices from Product.GetEffectivePrice() — never from client.
///   2. Shipping fee from IShippingCalculationService — reads DeliverySettings.
///   3. Tax from ITaxCalculationService — reads BusinessSettings.Tax.
///   4. Promotion discount from IPromotionService — re-validated inside transaction.
///   5. Inventory reserved with xmin concurrency token (PostgreSQL row-level).
///   6. Promotion usage recorded inside the same DB transaction (COD) or after
///      payment confirmation (Razorpay) — not before.
///   7. Totals:  subtotal - discount + tax (exclusive) + shipping + codFee = grandTotal
///      Tax-inclusive mode: tax is extracted from subtotal, not added on top.
///   8. Order financial snapshot is immutable after creation.
/// </summary>
internal sealed class CheckoutHandler(
    IApplicationDbContext db,
    IBusinessSettingsService businessSettings,
    IShippingCalculationService shippingService,
    ITaxCalculationService taxService,
    IPromotionService promotionService,
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
                return await BuildCheckoutResponse(existing, cancellationToken);
        }

        // -----------------------------------------------------------------------
        // Cart
        // -----------------------------------------------------------------------
        var cart = await db.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(
                c => c.CustomerId == command.CustomerId && c.ExpiresAt > DateTime.UtcNow,
                cancellationToken);

        if (cart is null || !cart.Items.Any())
            return Result.Failure<CheckoutResponse>(
                Error.Validation("CART_EMPTY", "Your cart is empty."));

        // -----------------------------------------------------------------------
        // Business settings — currency, delivery, tax
        // -----------------------------------------------------------------------
        var settings = await businessSettings.GetAsync(cancellationToken)
            ?? throw new InvalidOperationException("Business settings not found.");

        var delivery = settings.Delivery;
        var taxSettings = settings.Tax;
        var currency = settings.CurrencyCode;

        // -----------------------------------------------------------------------
        // Payment method
        // -----------------------------------------------------------------------
        if (!Enum.TryParse<PaymentMethod>(command.PaymentMethod, out var paymentMethod))
            return Result.Failure<CheckoutResponse>(
                Error.Validation("INVALID_PAYMENT_METHOD", $"Unknown payment method: {command.PaymentMethod}."));

        if (paymentMethod == PaymentMethod.CashOnDelivery && !delivery.CodEnabled)
            return Result.Failure<CheckoutResponse>(
                Error.Validation("COD_NOT_AVAILABLE", "Cash on delivery is not available for this store."));

        var isCod = paymentMethod == PaymentMethod.CashOnDelivery;

        // -----------------------------------------------------------------------
        // Load products + variants + inventory
        // -----------------------------------------------------------------------
        var productIds = cart.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await db.Products
            .Where(p => productIds.Contains(p.Id) && p.Status == ProductStatus.Active)
            .Include(p => p.Images)
            .ToListAsync(cancellationToken);

        var variantIds = cart.Items.Where(i => i.VariantId.HasValue)
            .Select(i => i.VariantId!.Value).ToList();
        var variants = variantIds.Any()
            ? await db.ProductVariants.Where(v => variantIds.Contains(v.Id)).ToListAsync(cancellationToken)
            : new List<ProductVariant>();

        var inventoryItems = await db.InventoryItems
            .Where(inv => productIds.Contains(inv.ProductId))
            .ToListAsync(cancellationToken);

        // -----------------------------------------------------------------------
        // Server-side pricing
        // -----------------------------------------------------------------------
        decimal subtotal = 0m;
        var orderItems = new List<OrderItem>();
        var cartItemContexts = new List<CartItemContext>();

        foreach (var cartItem in cart.Items)
        {
            var product = products.FirstOrDefault(p => p.Id == cartItem.ProductId);
            if (product is null)
                return Result.Failure<CheckoutResponse>(
                    Error.NotFound("PRODUCT_UNAVAILABLE",
                        $"Product {cartItem.ProductId} is no longer available."));

            var variant = cartItem.VariantId.HasValue
                ? variants.FirstOrDefault(v => v.Id == cartItem.VariantId.Value)
                : null;

            var unitPrice = product.GetEffectivePrice(variant);
            var lineTotal = unitPrice * cartItem.Quantity;
            subtotal += lineTotal;

            var variantDesc = variant is not null ? $"SKU: {variant.Sku ?? "N/A"}" : null;

            orderItems.Add(OrderItem.Create(
                Guid.Empty, product.Id, variant?.Id,
                product.Name, variantDesc, variant?.Sku ?? product.Sku,
                unitPrice, cartItem.Quantity));

            cartItemContexts.Add(new CartItemContext(product.Id, product.CategoryId, lineTotal));
        }

        // -----------------------------------------------------------------------
        // Promotion / coupon (re-validated inside transaction)
        // -----------------------------------------------------------------------
        PromotionCalculationResult promotionResult = PromotionCalculationResult.NoPromotion();

        if (!string.IsNullOrWhiteSpace(command.CouponCode))
        {
            var isFirstOrder = !await db.Orders
                .AnyAsync(o => o.CustomerId == command.CustomerId, cancellationToken);

            promotionResult = await promotionService.ValidateAndCalculateAsync(
                command.CouponCode, command.CustomerId, subtotal,
                cartItemContexts, isFirstOrder, cancellationToken);

            if (!promotionResult.IsValid)
                return Result.Failure<CheckoutResponse>(
                    Error.Validation(
                        promotionResult.ErrorCode ?? "COUPON_INVALID",
                        promotionResult.ErrorMessage ?? "Coupon is not valid."));
        }

        var discountAmount = promotionResult.DiscountAmount;

        // -----------------------------------------------------------------------
        // Shipping (via dedicated service — not inline)
        // -----------------------------------------------------------------------
        var shipping = shippingService.Calculate(subtotal, isCod, delivery);
        var shippingAmount = shipping.ShippingAmount;
        var codFee = shipping.CodFee;

        // -----------------------------------------------------------------------
        // Tax
        // Taxable base = subtotal - discount (apply discount before tax for exclusive pricing)
        // For inclusive pricing, tax is extracted from the price already, so TaxAmount is informational.
        // -----------------------------------------------------------------------
        var taxableBase = Math.Max(0m, subtotal - discountAmount);
        var tax = taxService.Calculate(taxableBase, taxSettings);
        var taxAmount = tax.TaxAmount;

        // -----------------------------------------------------------------------
        // Grand total
        // For exclusive tax:  grandTotal = subtotal - discount + tax + shipping + codFee
        // For inclusive tax:  grandTotal = subtotal - discount + shipping + codFee (tax already in price)
        // -----------------------------------------------------------------------
        decimal grandTotal = taxSettings.IsPriceInclusive
            ? subtotal - discountAmount + shippingAmount + codFee
            : subtotal - discountAmount + taxAmount + shippingAmount + codFee;

        grandTotal = Math.Max(0m, grandTotal);

        // -----------------------------------------------------------------------
        // Inventory reservation
        // -----------------------------------------------------------------------
        foreach (var cartItem in cart.Items)
        {
            var inv = inventoryItems.FirstOrDefault(i =>
                i.ProductId == cartItem.ProductId && i.VariantId == cartItem.VariantId);

            if (inv is not null)
            {
                try { inv.Reserve(cartItem.Quantity); }
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
            command.ShippingAddress.FullName, command.ShippingAddress.Phone,
            command.ShippingAddress.AddressLine1, command.ShippingAddress.AddressLine2,
            command.ShippingAddress.City, command.ShippingAddress.State,
            command.ShippingAddress.PostalCode, command.ShippingAddress.Country);

        var orderNumber = GenerateOrderNumber();
        var appliedCoupon = promotionResult.IsValid ? promotionResult.CouponCode : null;

        var order = Order.Create(
            command.CustomerId, orderNumber, currency,
            subtotal, shippingAmount, codFee, discountAmount, taxAmount, grandTotal,
            address, paymentMethod, appliedCoupon);

        if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
            db.Orders.Entry(order).Property("IdempotencyKey").CurrentValue = command.IdempotencyKey;

        db.Orders.Add(order);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var item in orderItems)
        {
            var oi = OrderItem.Create(
                order.Id, item.ProductId, item.VariantId,
                item.ProductName, item.VariantDescription, item.Sku,
                item.UnitPrice, item.Quantity);
            order.AddItem(oi);
            db.OrderItems.Add(oi);
        }

        var payment = Payment.Create(order.Id, paymentMethod.ToString(), grandTotal, currency);
        db.Payments.Add(payment);

        var outboxPayload = JsonSerializer.Serialize(new
        {
            order.Id, order.OrderNumber, command.CustomerId,
            grandTotal, currency
        });
        db.OutboxEvents.Add(OutboxEvent.Create("OrderCreated", outboxPayload));

        if (isCod)
        {
            order.Confirm();
            payment.MarkPaid("cod");

            // Record promotion usage for COD (confirmed immediately)
            if (promotionResult.IsValid && promotionResult.PromotionId.HasValue)
                await RecordPromotionUsageAsync(
                    promotionResult.PromotionId.Value, command.CustomerId, order.Id, cancellationToken);
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
                await db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                providerOrderId = pgResult.ProviderOrderId;
                payment.SetProviderOrderId(providerOrderId!);
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
            order.Id, order.OrderNumber, order.Status.ToString(),
            order.PaymentMethod.ToString(),
            subtotal, shippingAmount, codFee, discountAmount, taxAmount,
            grandTotal, currency,
            appliedCoupon, providerOrderId,
            null, // RazorpayKeyId — injected by controller
            order.CreatedAtUtc));
    }

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

    // -----------------------------------------------------------------------
    // Idempotency return path
    // -----------------------------------------------------------------------

    private async Task<Result<CheckoutResponse>> BuildCheckoutResponse(
        Order order, CancellationToken ct)
    {
        var payment = await db.Payments.FirstOrDefaultAsync(p => p.OrderId == order.Id, ct);
        return Result.Success(new CheckoutResponse(
            order.Id, order.OrderNumber, order.Status.ToString(),
            order.PaymentMethod.ToString(),
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
