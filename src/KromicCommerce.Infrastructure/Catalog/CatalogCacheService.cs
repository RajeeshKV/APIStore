using KromicCommerce.Application.Abstractions.Catalog;
using KromicCommerce.Infrastructure.Caching;

namespace KromicCommerce.Infrastructure.Catalog;

/// <summary>
/// IMemoryCache invalidation for catalog entities.
/// Removes known keys so the next read repopulates from PostgreSQL.
/// Invalidation must happen only after successful SaveChangesAsync.
/// </summary>
internal sealed class CatalogCacheService(IMemoryCache cache) : ICatalogCacheService
{
    // -----------------------------------------------------------------------
    // Admin catalog cache
    // -----------------------------------------------------------------------
    public void InvalidateCategories() => cache.Remove(CacheKeys.AllCategories);
    public void InvalidateBrands()     => cache.Remove(CacheKeys.AllBrands);
    public void InvalidateProducts()   => cache.Remove(CacheKeys.AllProducts);
    public void InvalidateProduct(Guid productId) => cache.Remove(CacheKeys.Product(productId));

    // -----------------------------------------------------------------------
    // Storefront catalog cache
    // -----------------------------------------------------------------------
    public void InvalidateStorefrontCategories() => cache.Remove(CacheKeys.StorefrontCategories);
    public void InvalidateStorefrontBrands()     => cache.Remove(CacheKeys.StorefrontBrands);
    public void InvalidateStorefrontFeatured()   => cache.Remove(CacheKeys.StorefrontFeatured);

    public void InvalidateStorefrontProduct(string slug)
    {
        if (!string.IsNullOrWhiteSpace(slug))
            cache.Remove(CacheKeys.StorefrontProduct(slug));
    }
}
