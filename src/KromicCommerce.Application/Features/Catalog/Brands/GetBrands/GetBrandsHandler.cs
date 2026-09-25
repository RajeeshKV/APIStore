using KromicCommerce.Application.Caching;
using KromicCommerce.Application.Features.Catalog.Brands.CreateBrand;
using Microsoft.Extensions.Caching.Memory;

namespace KromicCommerce.Application.Features.Catalog.Brands.GetBrands;

internal sealed class GetBrandsHandler(
    IApplicationDbContext db,
    IMemoryCache cache,
    IOptions<CatalogCacheOptions> cacheOpts)
    : IQueryHandler<GetBrandsQuery, IReadOnlyList<BrandResponse>>
{
    private const string CacheKey = CatalogCacheKeys.AllBrands;

    public async Task<Result<IReadOnlyList<BrandResponse>>> Handle(
        GetBrandsQuery query, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(CacheKey, out IReadOnlyList<BrandResponse>? cached) && cached is not null)
            return Result.Success(cached);

        var brands = await db.Brands
            .AsNoTracking()
            .OrderBy(b => b.Name)
            .ToListAsync(cancellationToken);

        var result = brands
            .Where(b => !query.ActiveOnly || b.IsActive)
            .Select(CreateBrandHandler.MapToResponse)
            .ToList()
            .AsReadOnly();

        cache.Set(CacheKey, (IReadOnlyList<BrandResponse>)result, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(cacheOpts.Value.DefaultExpiryMinutes),
            Size = 1
        });

        return Result.Success((IReadOnlyList<BrandResponse>)result);
    }
}

internal sealed class GetBrandBySlugHandler(IApplicationDbContext db)
    : IQueryHandler<GetBrandBySlugQuery, BrandResponse>
{
    public async Task<Result<BrandResponse>> Handle(
        GetBrandBySlugQuery query, CancellationToken cancellationToken)
    {
        var b = await db.Brands.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Slug == query.Slug, cancellationToken);
        return b is null
            ? Result.Failure<BrandResponse>(Error.NotFound("BRAND_NOT_FOUND", "Brand not found."))
            : Result.Success(CreateBrandHandler.MapToResponse(b));
    }
}

internal sealed class GetBrandByIdHandler(IApplicationDbContext db)
    : IQueryHandler<GetBrandByIdQuery, BrandResponse>
{
    public async Task<Result<BrandResponse>> Handle(
        GetBrandByIdQuery query, CancellationToken cancellationToken)
    {
        var b = await db.Brands.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == query.Id, cancellationToken);
        return b is null
            ? Result.Failure<BrandResponse>(Error.NotFound("BRAND_NOT_FOUND", "Brand not found."))
            : Result.Success(CreateBrandHandler.MapToResponse(b));
    }
}
