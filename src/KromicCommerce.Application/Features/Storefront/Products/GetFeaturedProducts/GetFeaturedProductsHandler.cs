using KromicCommerce.Application.Caching;
using Microsoft.Extensions.Caching.Memory;

namespace KromicCommerce.Application.Features.Storefront.Products.GetFeaturedProducts;

/// <summary>
/// Returns active featured products for homepage/banner sections.
/// Cached under "storefront:products:featured" with DefaultExpiryMinutes TTL.
/// </summary>
internal sealed class GetFeaturedProductsHandler(
    IApplicationDbContext db,
    IBusinessSettingsService businessSettings,
    IMemoryCache cache,
    IOptions<CatalogCacheOptions> cacheOpts)
    : IQueryHandler<GetFeaturedProductsQuery, IReadOnlyList<StorefrontProductSummaryResponse>>
{
    private const int MaxLimit = 50;
    private const string CacheKey = CatalogCacheKeys.StorefrontFeatured;

    public async Task<Result<IReadOnlyList<StorefrontProductSummaryResponse>>> Handle(
        GetFeaturedProductsQuery query,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, MaxLimit);

        if (cache.TryGetValue(CacheKey, out IReadOnlyList<StorefrontProductSummaryResponse>? cached)
            && cached is not null)
            return Result.Success(cached);

        var settings = await businessSettings.GetAsync(cancellationToken);
        var currency = settings?.CurrencyCode ?? "INR";

        var products = await db.Products
            .AsNoTracking()
            .Where(p => p.Status == ProductStatus.Active && p.IsFeatured)
            .OrderByDescending(p => p.CreatedAtUtc)
            .Take(limit)
            .Select(p => new
            {
                p.Id, p.Name, p.Slug, p.ShortDescription,
                p.Price, p.CompareAtPrice, p.IsFeatured,
                p.CategoryId,
                CategoryName = p.Category != null ? p.Category.Name : null,
                CategorySlug = p.Category != null ? p.Category.Slug : null,
                p.BrandId,
                BrandName = p.Brand != null ? p.Brand.Name : null,
                BrandSlug = p.Brand != null ? p.Brand.Slug : null,
                PrimaryImageUrl = p.Images
                    .OrderBy(i => i.SortOrder)
                    .Where(i => i.IsPrimary)
                    .Select(i => i.Asset.SecureUrl)
                    .FirstOrDefault()
                    ?? p.Images.OrderBy(i => i.SortOrder).Select(i => i.Asset.SecureUrl).FirstOrDefault(),
                Inventory = db.InventoryItems
                    .Where(inv => inv.ProductId == p.Id && inv.VariantId == null)
                    .Select(inv => new { inv.OnHand, inv.Reserved, inv.LowStockThreshold })
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var items = products.Select(p =>
        {
            StockAvailability avail;
            bool canPurchase;
            if (p.Inventory is null) { avail = StockAvailability.InStock; canPurchase = true; }
            else
            {
                var a = p.Inventory.OnHand - p.Inventory.Reserved;
                avail = a <= 0 ? StockAvailability.OutOfStock
                    : (a <= p.Inventory.LowStockThreshold ? StockAvailability.LowStock : StockAvailability.InStock);
                canPurchase = a > 0;
            }
            return new StorefrontProductSummaryResponse(
                p.Id, p.Name, p.Slug, p.ShortDescription,
                p.Price, p.CompareAtPrice, currency,
                p.PrimaryImageUrl, avail, canPurchase,
                p.CategoryId, p.CategoryName, p.CategorySlug,
                p.BrandId, p.BrandName, p.BrandSlug,
                p.IsFeatured);
        }).ToList();

        IReadOnlyList<StorefrontProductSummaryResponse> result = items;
        cache.Set(CacheKey, result, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow =
                TimeSpan.FromMinutes(cacheOpts.Value.DefaultExpiryMinutes),
            Size = 1
        });

        return Result.Success(result);
    }
}
