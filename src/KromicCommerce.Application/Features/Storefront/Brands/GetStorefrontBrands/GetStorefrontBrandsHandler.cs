using KromicCommerce.Application.Caching;
using Microsoft.Extensions.Caching.Memory;

namespace KromicCommerce.Application.Features.Storefront.Brands.GetStorefrontBrands;

/// <summary>
/// Returns active brands with active-product counts.
/// Cached under "storefront:brands:all".
/// </summary>
internal sealed class GetStorefrontBrandsHandler(
    IApplicationDbContext db,
    IMemoryCache cache,
    IOptions<CatalogCacheOptions> cacheOpts)
    : IQueryHandler<GetStorefrontBrandsQuery, IReadOnlyList<StorefrontBrandResponse>>
{
    private const string CacheKey = CatalogCacheKeys.StorefrontBrands;

    public async Task<Result<IReadOnlyList<StorefrontBrandResponse>>> Handle(
        GetStorefrontBrandsQuery query,
        CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(CacheKey, out IReadOnlyList<StorefrontBrandResponse>? cached)
            && cached is not null)
            return Result.Success(cached);

        var brands = await db.Brands
            .AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.Name)
            .Select(b => new
            {
                b.Id, b.Name, b.Slug, b.Description, b.WebsiteUrl, b.LogoUrl,
                ProductCount = db.Products.Count(p =>
                    p.BrandId == b.Id && p.Status == ProductStatus.Active)
            })
            .ToListAsync(cancellationToken);

        IReadOnlyList<StorefrontBrandResponse> result = brands
            .Select(b => new StorefrontBrandResponse(
                b.Id, b.Name, b.Slug, b.Description, b.WebsiteUrl, b.LogoUrl, b.ProductCount))
            .ToList();

        cache.Set(CacheKey, result, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow =
                TimeSpan.FromMinutes(cacheOpts.Value.DefaultExpiryMinutes),
            Size = 1
        });

        return Result.Success(result);
    }
}
