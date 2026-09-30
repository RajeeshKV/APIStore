using KromicCommerce.Application.Abstractions.Catalog;
using KromicCommerce.Application.Caching;
using KromicCommerce.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Memory;

namespace KromicCommerce.Infrastructure.Catalog;

/// <summary>
/// IMemoryCache invalidation for catalog entities.
/// Removes known keys so the next read repopulates from PostgreSQL.
/// Invalidation must happen only after successful SaveChangesAsync.
///
/// The <c>*Graph</c> methods encode the dependency map between cached projections so a
/// single call site cannot forget a dependent entry (e.g. brand ProductCount going stale
/// after a product status change).
/// </summary>
internal sealed class CatalogCacheService(IMemoryCache cache) : ICatalogCacheService
{
    // -----------------------------------------------------------------------
    // Admin catalog cache
    // -----------------------------------------------------------------------
    public void InvalidateCategories()
    {
        cache.Remove(CacheKeys.AllCategories);
        cache.Remove(CatalogCacheKeys.AdminCategories(activeOnly: true));
        cache.Remove(CatalogCacheKeys.AdminCategories(activeOnly: false));
    }

    public void InvalidateBrands()
    {
        cache.Remove(CacheKeys.AllBrands);
        cache.Remove(CatalogCacheKeys.AdminBrands(activeOnly: true));
        cache.Remove(CatalogCacheKeys.AdminBrands(activeOnly: false));
    }

    public void InvalidateProducts() => cache.Remove(CacheKeys.AllProducts);

    public void InvalidateProduct(Guid productId) => cache.Remove(CacheKeys.Product(productId));

    private const int MaxFeaturedLimit = 50;

    // -----------------------------------------------------------------------
    // Storefront catalog cache
    // -----------------------------------------------------------------------
    public void InvalidateStorefrontCategories() => cache.Remove(CacheKeys.StorefrontCategories);
    public void InvalidateStorefrontBrands()     => cache.Remove(CacheKeys.StorefrontBrands);

    public void InvalidateStorefrontFeatured()
    {
        // Featured entries are keyed per requested limit, and IMemoryCache has no prefix
        // delete, so every permitted limit is removed explicitly. MaxFeaturedLimit mirrors the
        // clamp in GetFeaturedProductsHandler; going slightly over is harmless.
        for (var limit = 1; limit <= MaxFeaturedLimit; limit++)
            cache.Remove(CacheKeys.StorefrontFeatured(limit));
    }

    public void InvalidateStorefrontProduct(string slug) => RemoveStorefrontProduct(slug);

    // -----------------------------------------------------------------------
    // Dependency-aware (graph) invalidation
    // -----------------------------------------------------------------------
    public void InvalidateProductGraph()
    {
        InvalidateProducts();
        InvalidateStorefrontFeatured();
        // Brand and category projections embed ProductCount, which is derived from products.
        InvalidateStorefrontBrands();
        InvalidateStorefrontCategories();
    }

    public void InvalidateProductGraph(Guid productId, string? slug)
    {
        InvalidateProduct(productId);
        RemoveStorefrontProduct(slug);
        InvalidateStorefrontFeatured();
        InvalidateStorefrontBrands();
        InvalidateStorefrontCategories();
    }

    public void InvalidateStockGraph(string? slug)
    {
        RemoveStorefrontProduct(slug);
        // The featured list projects per-product availability, so it goes stale with the
        // product page whenever stock changes.
        InvalidateStorefrontFeatured();
    }

    public void InvalidateBrandGraph()
    {
        InvalidateBrands();
        InvalidateStorefrontBrands();
    }

    public void InvalidateCategoryGraph()
    {
        InvalidateCategories();
        InvalidateStorefrontCategories();
    }

    public void InvalidateShippingConfiguration()
    {
        // Bumping the epoch orphans every delivery-scoped storefront product entry.
        // The absolute expiry / size limit reclaims the orphans.
        cache.Set(CatalogCacheKeys.ShippingEpochKey, GetShippingEpoch() + 1, new MemoryCacheEntryOptions
        {
            Size = 1
        });
    }

    public int GetShippingEpoch() =>
        cache.TryGetValue(CatalogCacheKeys.ShippingEpochKey, out int epoch) ? epoch : 0;

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Removes every delivery-scoped variant of a storefront product key. Only the
    /// current epoch's key can exist under normal operation, but removing the previously
    /// scoped key too keeps the cache tight after an epoch bump.
    /// </summary>
    private void RemoveStorefrontProduct(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug)) return;
        cache.Remove(CacheKeys.StorefrontProduct(slug));
        cache.Remove(CatalogCacheKeys.DeliveryScopedStorefrontProduct(slug, GetShippingEpoch()));
    }
}
