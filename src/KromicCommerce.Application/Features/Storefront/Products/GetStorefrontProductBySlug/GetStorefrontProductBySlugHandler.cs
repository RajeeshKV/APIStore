using KromicCommerce.Application.Caching;
using Microsoft.Extensions.Caching.Memory;

namespace KromicCommerce.Application.Features.Storefront.Products.GetStorefrontProductBySlug;

/// <summary>
/// Returns full product detail for a storefront product page.
/// Caches the public response by slug (key: "storefront:product:{slug}").
/// Draft / Archived products return NotFound — never exposed publicly.
/// Effective prices use Product.GetEffectivePrice — single source of truth.
/// Stock is derived per-product and per-variant without exposing OnHand/Reserved.
/// </summary>
internal sealed class GetStorefrontProductBySlugHandler(
    IApplicationDbContext db,
    IBusinessSettingsService businessSettings,
    IStorefrontStockService stockService,
    IDeliveryEstimateService deliveryEstimate,
    ICatalogCacheService catalogCache,
    IMemoryCache cache,
    IOptions<CatalogCacheOptions> cacheOpts,
    ILogger<GetStorefrontProductBySlugHandler> logger)
    : IQueryHandler<GetStorefrontProductBySlugQuery, StorefrontProductResponse>
{
    public async Task<Result<StorefrontProductResponse>> Handle(
        GetStorefrontProductBySlugQuery query,
        CancellationToken cancellationToken)
    {
        // Normalise the slug before it is used as a cache key as well as in the query,
        // otherwise a whitespace-padded slug writes a second, unreachable cache entry.
        var slug = CatalogCacheKeys.NormaliseSlug(query.Slug);
        var cacheKey = CatalogCacheKeys.DeliveryScopedStorefrontProduct(
            slug, catalogCache.GetShippingEpoch());

        if (cache.TryGetValue(cacheKey, out StorefrontProductResponse? cached) && cached is not null)
            return Result.Success(cached);

        // -----------------------------------------------------------------------
        // Load product — Active only, full detail
        // -----------------------------------------------------------------------
        var product = await db.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Images)
            .Include(p => p.Attributes).ThenInclude(a => a.Values)
            .Include(p => p.Variants)
            .FirstOrDefaultAsync(
                p => p.Slug == slug && p.Status == ProductStatus.Active,
                cancellationToken);

        if (product is null)
            return Result.Failure<StorefrontProductResponse>(
                Error.NotFound("PRODUCT_NOT_FOUND", "Product not found."));

        // -----------------------------------------------------------------------
        // Business settings: currency + delivery config
        // -----------------------------------------------------------------------
        var settings = await businessSettings.GetAsync(cancellationToken);
        var currency = settings?.CurrencyCode ?? "INR";
        DeliveryEstimateDto? estimate = null;
        if (settings is not null)
        {
            var d = settings.Delivery;
            estimate = deliveryEstimate.Calculate(
                d.ProcessingDays, d.MinDeliveryDays, d.MaxDeliveryDays);
        }

        // -----------------------------------------------------------------------
        // Base product inventory (VariantId == null)
        // -----------------------------------------------------------------------
        var baseInventory = await db.InventoryItems
            .AsNoTracking()
            .FirstOrDefaultAsync(
                inv => inv.ProductId == product.Id && inv.VariantId == null,
                cancellationToken);

        var baseStock = stockService.GetStockResponse(baseInventory);

        // -----------------------------------------------------------------------
        // Per-variant inventory
        // -----------------------------------------------------------------------
        var variantInventories = await db.InventoryItems
            .AsNoTracking()
            .Where(inv => inv.ProductId == product.Id && inv.VariantId != null)
            .ToListAsync(cancellationToken);

        var variantInventoryMap = variantInventories
            .ToDictionary(inv => inv.VariantId!.Value);

        // -----------------------------------------------------------------------
        // Map images — ordered by SortOrder
        // -----------------------------------------------------------------------
        var images = product.Images
            .OrderBy(i => i.SortOrder)
            .Select(i => new StorefrontImageResponse(
                i.Id, i.Asset.SecureUrl, i.Asset.AltText, i.SortOrder, i.IsPrimary))
            .ToList();

        // -----------------------------------------------------------------------
        // Map variants — effective price, per-variant stock, resolved option labels
        // -----------------------------------------------------------------------
        // A variant carrying an unknown attribute value still appears, but with a smaller
        // option list rather than being dropped, so a stale reference cannot hide a SKU.
        var variantAttributeMap = await Features.Catalog.Products.Variants.VariantAttributeHelper
            .ResolveAsync(db, product.Variants
                .SelectMany(v => v.ParsedAttributeValueIds)
                .Distinct()
                .ToList(), cancellationToken);

        var variants = product.Variants
            .OrderBy(v => v.SortOrder).ThenBy(v => v.CreatedAtUtc)
            .Select(v =>
            {
                variantInventoryMap.TryGetValue(v.Id, out var vInv);
                var vStock = stockService.GetStockResponse(vInv);
                var effectivePrice = product.GetEffectivePrice(v);

                // An inactive variant is never purchasable, whatever its stock says.
                var canPurchase = v.IsActive && vStock.CanPurchase;

                IReadOnlyList<VariantAttributeValueResponse> attributes =
                    v.ParsedAttributeValueIds
                        .Where(variantAttributeMap.ContainsKey)
                        .Select(id => variantAttributeMap[id])
                        .ToList();

                return new StorefrontVariantResponse(
                    v.Id, v.Sku, effectivePrice,
                    v.SortOrder, v.IsActive, v.AttributeValueIds,
                    vStock.Availability, canPurchase, attributes);
            })
            .ToList();

        // -----------------------------------------------------------------------
        // Map attributes — ordered
        // -----------------------------------------------------------------------
        var attributes = product.Attributes
            .OrderBy(a => a.SortOrder)
            .Select(a => new ProductAttributeDto(
                a.Id, a.Name, a.SortOrder,
                a.Values.OrderBy(v => v.SortOrder)
                    .Select(v => new AttributeValueDto(v.Id, v.Value, v.SortOrder))
                    .ToList()))
            .ToList();

        // -----------------------------------------------------------------------
        // Product-level availability
        //
        // When a product has variants, the purchasable stock lives on the variants — the base
        // inventory row is typically absent or unstocked. Reporting only the base row would
        // therefore advertise a product as buyable while every option is sold out, or hide a
        // variant that is in stock. Variants win when present; the base row is the fallback
        // for products without variants, which keeps those products behaving exactly as before.
        // -----------------------------------------------------------------------
        var productAvailability = variants.Count > 0
            ? new PublicStockResponse(
                Availability: RollUpVariantAvailability(variants),
                CanPurchase: variants.Any(v => v.CanPurchase))
            : baseStock;

        // -----------------------------------------------------------------------
        // Build response
        // -----------------------------------------------------------------------
        var response = new StorefrontProductResponse(
            product.Id, product.Name, product.Slug,
            product.Description, product.ShortDescription,
            product.Price, product.CompareAtPrice, currency,
            productAvailability.Availability, productAvailability.CanPurchase,
            product.CategoryId, product.Category?.Name, product.Category?.Slug,
            product.BrandId, product.Brand?.Name, product.Brand?.Slug,
            product.IsFeatured,
            images, attributes, variants,
            estimate,
            product.MetaTitle, product.MetaDescription, product.MetaKeywords);

        // Cache the public response under a shipping-configuration scoped key, so that a
        // shipping or COD change invalidates every product page (the response embeds a
        // delivery estimate) without the cache having to know which slugs are cached.
        cache.Set(cacheKey, response, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow =
                TimeSpan.FromMinutes(cacheOpts.Value.DefaultExpiryMinutes),
            Size = 1
        });

        logger.LogDebug("Storefront product detail loaded and cached: {Slug}", query.Slug);
        return Result.Success(response);
    }

    /// <summary>
    /// Rolls per-variant availability up to a product-level value:
    /// any variant in stock → InStock; variants exist but none in stock → OutOfStock.
    /// A low-stock-only rollup is deliberately not surfaced — it is a per-variant signal and
    /// the product-level field is a coarse "can this be bought at all" indicator.
    /// </summary>
    private static StockAvailability RollUpVariantAvailability(
        IReadOnlyList<StorefrontVariantResponse> variants)
    {
        if (variants.Any(v => v.StockAvailability == StockAvailability.InStock))
            return StockAvailability.InStock;

        return variants.Any(v => v.StockAvailability == StockAvailability.LowStock)
            ? StockAvailability.LowStock
            : StockAvailability.OutOfStock;
    }
}
