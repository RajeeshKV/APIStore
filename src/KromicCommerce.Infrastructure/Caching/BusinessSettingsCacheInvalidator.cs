using KromicCommerce.Application.Abstractions.Catalog;
using KromicCommerce.Application.Abstractions.Store;
using Microsoft.Extensions.Logging;

namespace KromicCommerce.Infrastructure.Caching;

/// <summary>
/// IMemoryCache eviction for the business settings caches.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately cache-only: the dependencies are <see cref="IMemoryCache"/> and
/// <see cref="ICatalogCacheService"/>, neither of which can reach <c>AppDbContext</c>. That
/// restriction is the whole point of the type — it is resolved while <c>AppDbContext</c> is being
/// constructed, through the EF save interceptor, so anything it pulled in that needed the context
/// would deadlock the container.
/// </para>
/// <para>
/// The eviction logic lives here rather than in <c>BusinessSettingsService</c> so that the
/// interceptor and the settings handlers invalidate the same keys through one implementation.
/// </para>
/// </remarks>
internal sealed class BusinessSettingsCacheInvalidator(
    IMemoryCache cache,
    ICatalogCacheService catalogCache,
    ILogger<BusinessSettingsCacheInvalidator> logger) : IBusinessSettingsCacheInvalidator
{
    public void Invalidate()
    {
        cache.Remove(CacheKeys.BusinessSettings);
        logger.LogDebug("BusinessSettings cache invalidated.");
    }

    public void InvalidateShipping()
    {
        Invalidate();

        // Storefront product pages embed a delivery estimate derived from DeliverySettings, so they
        // must go stale together with the settings object itself. IMemoryCache cannot prefix-delete,
        // so the catalog cache orphans them by bumping the shipping epoch.
        catalogCache.InvalidateShippingConfiguration();

        logger.LogDebug(
            "Shipping/COD configuration caches invalidated (epoch {Epoch}).",
            catalogCache.GetShippingEpoch());
    }
}
