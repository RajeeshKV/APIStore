using KromicCommerce.Application.Abstractions.Catalog;
using KromicCommerce.Application.Abstractions.Store;
using KromicCommerce.Application.Caching;
using Microsoft.Extensions.Logging;

namespace KromicCommerce.Infrastructure.Caching;

/// <summary>
/// Translates a <see cref="CacheInvalidationPlan"/> into calls on the existing
/// <see cref="ICatalogCacheService"/> vocabulary.
/// </summary>
/// <remarks>
/// <para>
/// The plan is the dependency knowledge; this class is the mechanism. Keeping the translation in
/// one place means the epoch-based and key-based projections are always evicted the same way
/// regardless of which entity triggered the change.
/// </para>
/// <para>
/// The dependencies are cache abstractions and nothing else. This type is constructed while
/// <c>AppDbContext</c> is being built (the EF save interceptor owns it), so a dependency that
/// needed the context — <see cref="IBusinessSettingsService"/> most obviously — would make the
/// container's construction of the context re-enter itself and deadlock. That is why the settings
/// caches are evicted through <see cref="IBusinessSettingsCacheInvalidator"/> rather than through
/// the settings service that reads and writes them.
/// </para>
/// </remarks>
internal sealed class CacheInvalidator(
    ICatalogCacheService catalogCache,
    IBusinessSettingsCacheInvalidator businessSettingsCache,
    ILogger<CacheInvalidator> logger) : ICacheInvalidator
{
    public ValueTask ApplyAsync(
        CacheInvalidationPlan plan, CancellationToken cancellationToken = default)
    {
        if (plan.IsEmpty)
            return ValueTask.CompletedTask;

        var p = plan.Projections;

        // Parameterised projections first, while the epochs still address the entries that exist.
        // Order does not actually matter for correctness — bumping an epoch already orphans the
        // entries a following per-slug removal would have targeted — but reading top-down keeps
        // the intent obvious.
        if (p.HasFlag(CacheProjection.StorefrontProduct))
            foreach (var slug in plan.ProductSlugs)
                catalogCache.InvalidateStorefrontProduct(slug);

        if (p.HasFlag(CacheProjection.ProductReviews))
            foreach (var productId in plan.ProductIds)
                catalogCache.InvalidateProductReviews(productId);

        if (p.HasFlag(CacheProjection.StorefrontFeatured))
            catalogCache.InvalidateStorefrontFeatured();

        if (p.HasFlag(CacheProjection.StorefrontCategories))
            catalogCache.InvalidateStorefrontCategories();

        if (p.HasFlag(CacheProjection.StorefrontBrands))
            catalogCache.InvalidateStorefrontBrands();

        if (p.HasFlag(CacheProjection.AdminCategories))
            catalogCache.InvalidateCategories();

        if (p.HasFlag(CacheProjection.AdminBrands))
            catalogCache.InvalidateBrands();

        if (p.HasFlag(CacheProjection.StorefrontCarousel))
            catalogCache.InvalidateCarousel();

        if (p.HasFlag(CacheProjection.PublicPolicies))
            catalogCache.InvalidatePublicPolicies();

        // Product pages embed the currency code from BusinessSettings, and a category or brand
        // write invalidates the names and slugs every page renders. Both orphan every cached page
        // at once via the catalog epoch, which is O(1) rather than proportional to the number of
        // cached products — the slugs that would need evicting cannot be enumerated from the cache.
        if (p.HasFlag(CacheProjection.AllStorefrontProducts))
            catalogCache.InvalidateCatalogStructure();

        // Delivery estimates on every product page derive from Delivery, which is why this bumps
        // the shipping epoch instead of only dropping the settings object.
        if (p.HasFlag(CacheProjection.ShippingConfiguration))
            businessSettingsCache.InvalidateShipping();
        else if (p.HasFlag(CacheProjection.BusinessSettings))
            businessSettingsCache.Invalidate();

        logger.LogDebug(
            "Cache invalidated {Plan}; slugs: {Slugs}, products: {Products}.",
            p, plan.ProductSlugs.Count, plan.ProductIds.Count);

        return ValueTask.CompletedTask;
    }
}