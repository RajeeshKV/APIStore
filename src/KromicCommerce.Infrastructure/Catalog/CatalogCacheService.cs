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

    // The featured-page limit cap lives in CatalogCacheKeys so that the query handler's clamp and
    // the invalidation loop below cannot drift apart.
    private const int MaxFeaturedLimit = CatalogCacheKeys.MaxFeaturedLimit;

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

    public void InvalidateCarousel() => cache.Remove(CatalogCacheKeys.StorefrontCarousel);

    public void InvalidateProductReviews(Guid productId)
    {
        // Epoch bump rather than key enumeration. IMemoryCache cannot prefix-delete, and one
        // product can have cached pages for every (sort, rating filter, page) combination.
        // Every previously written entry becomes unreachable and is reclaimed by the cache's
        // size limit / absolute expiry.
        cache.Set(
            CatalogCacheKeys.ProductReviewsEpochKey(productId),
            GetProductReviewsEpoch(productId) + 1,
            new MemoryCacheEntryOptions { Size = 1 });
    }

    private int GetProductReviewsEpoch(Guid productId) =>
        cache.TryGetValue(CatalogCacheKeys.ProductReviewsEpochKey(productId), out int epoch) ? epoch : 0;

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
        // Storefront product pages embed BrandName/BrandSlug, so renaming or deactivating a brand
        // makes every cached product page wrong. The affected slugs are not knowable here, so the
        // catalog epoch is bumped instead, orphaning all of them in O(1).
        InvalidateCatalogStructure();
    }

    public void InvalidateCategoryGraph()
    {
        InvalidateCategories();
        InvalidateStorefrontCategories();
        // Same reasoning as InvalidateBrandGraph: product pages embed CategoryName/CategorySlug.
        InvalidateCatalogStructure();
    }

    /// <summary>
    /// Orphans every cached storefront product page and featured list.
    /// </summary>
    /// <remarks>
    /// Used for mutations whose blast radius spans products that cannot be enumerated from the
    /// cache alone — a brand or category rename or deactivation. Product pages embed the brand and
    /// category names, so evicting only the brand/category lists would leave every product page
    /// showing the old name until the entry's absolute expiry.
    /// </remarks>
    public void InvalidateCatalogStructure()
    {
        cache.Set(
            CatalogCacheKeys.CatalogEpochKey,
            GetCatalogEpoch() + 1,
            new MemoryCacheEntryOptions { Size = 1 });

        // The featured list is keyed per limit rather than through the epoch, so it is evicted
        // explicitly here to keep the two mechanisms from diverging.
        InvalidateStorefrontFeatured();
    }

    public int GetCatalogEpoch() => CatalogCacheKeys.CurrentCatalogEpoch(cache);

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
    /// Removes every known variant of a storefront product key — the unscoped key, plus the
    /// shipping- and catalog-scoped keys for the current epochs.
    /// </summary>
    /// <remarks>
    /// Entries written under earlier epochs are already unreachable and are left to the cache's
    /// size limit and absolute expiry rather than being enumerated and removed.
    /// </remarks>
    private void RemoveStorefrontProduct(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug)) return;

        cache.Remove(CacheKeys.StorefrontProduct(slug));
        cache.Remove(CatalogCacheKeys.DeliveryScopedStorefrontProduct(slug, GetShippingEpoch()));
        cache.Remove(
            CatalogCacheKeys.CatalogScopedStorefrontProduct(slug, GetCatalogEpoch(), GetShippingEpoch()));
    }
}
