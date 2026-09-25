namespace KromicCommerce.Application.Features.Cart.GetCart;

internal sealed class GetCartHandler(
    IApplicationDbContext db,
    IBusinessSettingsService businessSettings,
    IStorefrontStockService stockService)
    : IQueryHandler<GetCartQuery, CartResponse>
{
    public async Task<Result<CartResponse>> Handle(
        GetCartQuery query, CancellationToken cancellationToken)
    {
        var settings = await businessSettings.GetAsync(cancellationToken);
        var currency = settings?.CurrencyCode ?? "INR";

        // Resolve the cart by customer or anonymous ID
        KromicCommerce.Domain.Cart.Cart? cart = null;
        if (query.CustomerId.HasValue)
            cart = await db.Carts
                .AsNoTracking()
                .Include(c => c.Items)
                .FirstOrDefaultAsync(
                    c => c.CustomerId == query.CustomerId.Value && c.ExpiresAt > DateTime.UtcNow,
                    cancellationToken);
        else if (!string.IsNullOrWhiteSpace(query.AnonymousCartId))
            cart = await db.Carts
                .AsNoTracking()
                .Include(c => c.Items)
                .FirstOrDefaultAsync(
                    c => c.AnonymousId == query.AnonymousCartId && c.ExpiresAt > DateTime.UtcNow,
                    cancellationToken);

        if (cart is null || !cart.Items.Any())
        {
            var emptyId = cart?.Id ?? Guid.Empty;
            return Result.Success(new CartResponse(emptyId, [], 0m, currency, 0, true));
        }

        // Load products and variants for pricing — server is authoritative
        var productIds = cart.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await db.Products
            .AsNoTracking()
            .Where(p => productIds.Contains(p.Id) && p.Status == ProductStatus.Active)
            .Include(p => p.Images)
            .ToListAsync(cancellationToken);

        var variantIds = cart.Items.Where(i => i.VariantId.HasValue)
            .Select(i => i.VariantId!.Value).Distinct().ToList();
        var variants = variantIds.Any()
            ? await db.ProductVariants.AsNoTracking()
                .Where(v => variantIds.Contains(v.Id)).ToListAsync(cancellationToken)
            : [];

        var inventoryItems = await db.InventoryItems.AsNoTracking()
            .Where(inv => productIds.Contains(inv.ProductId))
            .ToListAsync(cancellationToken);

        var items = new List<CartItemResponse>();
        decimal subtotal = 0m;

        foreach (var cartItem in cart.Items)
        {
            var product = products.FirstOrDefault(p => p.Id == cartItem.ProductId);
            if (product is null) continue; // product became inactive — skip silently

            var variant = cartItem.VariantId.HasValue
                ? variants.FirstOrDefault(v => v.Id == cartItem.VariantId.Value)
                : null;

            var unitPrice = product.GetEffectivePrice(variant);
            var lineTotal = unitPrice * cartItem.Quantity;
            subtotal += lineTotal;

            var inv = inventoryItems.FirstOrDefault(i =>
                i.ProductId == cartItem.ProductId &&
                i.VariantId == cartItem.VariantId);
            var stockResp = stockService.GetStockResponse(inv);

            var primaryImageUrl = product.Images
                .OrderBy(i => i.SortOrder)
                .FirstOrDefault(i => i.IsPrimary)?.Asset.SecureUrl
                ?? product.Images.OrderBy(i => i.SortOrder).FirstOrDefault()?.Asset.SecureUrl;

            var variantDesc = variant is not null
                ? (variant.Sku is not null ? $"SKU: {variant.Sku}" : null)
                : null;

            items.Add(new CartItemResponse(
                cartItem.Id,
                product.Id, product.Name, product.Slug,
                variant?.Id, variantDesc, variant?.Sku ?? product.Sku,
                unitPrice, cartItem.Quantity, lineTotal, currency,
                stockResp.Availability, stockResp.CanPurchase,
                primaryImageUrl));
        }

        return Result.Success(new CartResponse(
            cart.Id, items, subtotal, currency, items.Count, items.Count == 0));
    }
}
