using KromicCommerce.Application.Caching;
using Microsoft.Extensions.Caching.Memory;

namespace KromicCommerce.Application.Features.Storefront.Categories.GetStorefrontCategories;

/// <summary>
/// Returns active categories with active-product counts.
/// Cached under "storefront:categories:all".
/// ProductCount only includes Active products.
/// </summary>
internal sealed class GetStorefrontCategoriesHandler(
    IApplicationDbContext db,
    IMemoryCache cache,
    IOptions<CatalogCacheOptions> cacheOpts)
    : IQueryHandler<GetStorefrontCategoriesQuery, IReadOnlyList<StorefrontCategoryResponse>>
{
    private const string CacheKey = CatalogCacheKeys.StorefrontCategories;

    public async Task<Result<IReadOnlyList<StorefrontCategoryResponse>>> Handle(
        GetStorefrontCategoriesQuery query,
        CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(CacheKey, out IReadOnlyList<StorefrontCategoryResponse>? cached)
            && cached is not null)
            return Result.Success(cached);

        var categories = await db.Categories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new
            {
                c.Id, c.Name, c.Slug, c.Description,
                c.ParentCategoryId, c.SortOrder, c.ImageUrl,
                ParentName = c.ParentCategory != null ? c.ParentCategory.Name : null,
                ProductCount = db.Products.Count(p =>
                    p.CategoryId == c.Id && p.Status == ProductStatus.Active)
            })
            .ToListAsync(cancellationToken);

        IReadOnlyList<StorefrontCategoryResponse> result = categories
            .Select(c => new StorefrontCategoryResponse(
                c.Id, c.Name, c.Slug, c.Description,
                c.ParentCategoryId, c.ParentName,
                c.SortOrder, c.ImageUrl, c.ProductCount))
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
