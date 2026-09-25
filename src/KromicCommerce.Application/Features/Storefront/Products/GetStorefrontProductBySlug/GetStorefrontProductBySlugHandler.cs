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
    IMemoryCache cache,
    IOptions<CatalogCacheOptions> cacheOpts,
    ILogger<GetStorefrontProductBySlugHandler> logger)
    : IQueryHandler<GetStorefrontProductBySlugQuery, StorefrontProductResponse>
{
    public async Task<Result<StorefrontProductResponse>> Handle(
        GetStorefrontProductBySlugQuery query,
        CancellationToken cancellationToken)
    {
        var cacheKey = CatalogCacheKeys.StorefrontProduct(query.Slug);

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
                p => p.Slug == query.Slug.Trim().ToLowerInvariant()
                     && p.Status == ProductStatus.Active,
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
        // Map variants — effective price + per-variant stock
        // -----------------------------------------------------------------------
        var variants = product.Variants
            .OrderBy(v => v.SortOrder)
            .Select(v =>
            {
                variantInventoryMap.TryGetValue(v.Id, out var vInv);
                var vStock = stockService.GetStockResponse(vInv);
                var effectivePrice = product.GetEffectivePrice(v);
                return new StorefrontVariantResponse(
                    v.Id, v.Sku, effectivePrice,
                    v.SortOrder, v.IsActive, v.AttributeValueIds,
                    vStock.Availability, vStock.CanPurchase);
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
        // Build response
        // -----------------------------------------------------------------------
        var response = new StorefrontProductResponse(
            product.Id, product.Name, product.Slug,
            product.Description, product.ShortDescription,
            product.Price, product.CompareAtPrice, currency,
            baseStock.Availability, baseStock.CanPurchase,
            product.CategoryId, product.Category?.Name, product.Category?.Slug,
            product.BrandId, product.Brand?.Name, product.Brand?.Slug,
            product.IsFeatured,
            images, attributes, variants,
            estimate,
            product.MetaTitle, product.MetaDescription, product.MetaKeywords);

        // Cache the public response
        cache.Set(cacheKey, response, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow =
                TimeSpan.FromMinutes(cacheOpts.Value.DefaultExpiryMinutes),
            Size = 1
        });

        logger.LogDebug("Storefront product detail loaded and cached: {Slug}", query.Slug);
        return Result.Success(response);
    }
}
