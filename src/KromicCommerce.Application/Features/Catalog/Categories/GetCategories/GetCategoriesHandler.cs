using KromicCommerce.Application.Abstractions.Catalog;
using KromicCommerce.Application.Caching;
using KromicCommerce.Application.Features.Catalog.Categories.CreateCategory;
using Microsoft.Extensions.Caching.Memory;

namespace KromicCommerce.Application.Features.Catalog.Categories.GetCategories;

internal sealed class GetCategoriesHandler(
    IApplicationDbContext db,
    IMemoryCache cache,
    IOptions<CatalogCacheOptions> cacheOpts)
    : IQueryHandler<GetCategoriesQuery, IReadOnlyList<CategoryResponse>>
{
    private const string CacheKey = CatalogCacheKeys.AllCategories;

    public async Task<Result<IReadOnlyList<CategoryResponse>>> Handle(
        GetCategoriesQuery query,
        CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(CacheKey, out IReadOnlyList<CategoryResponse>? cached) && cached is not null)
            return Result.Success(cached);

        var categories = await db.Categories
            .AsNoTracking()
            .Include(c => c.ParentCategory)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);

        var result = categories
            .Where(c => !query.ActiveOnly || c.IsActive)
            .Select(c => CreateCategoryHandler.MapToResponse(c, c.ParentCategory?.Name))
            .ToList()
            .AsReadOnly();

        cache.Set(CacheKey, (IReadOnlyList<CategoryResponse>)result, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(cacheOpts.Value.DefaultExpiryMinutes),
            Size = 1
        });

        return Result.Success((IReadOnlyList<CategoryResponse>)result);
    }
}
