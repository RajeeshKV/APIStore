using KromicCommerce.Application.Abstractions.Store;
using KromicCommerce.Infrastructure.Caching;
using KromicCommerce.Infrastructure.Configuration;
using KromicCommerce.Infrastructure.Persistence;

namespace KromicCommerce.Infrastructure.Store;

/// <summary>
/// Cache-aside implementation of IBusinessSettingsService.
/// Read:  IMemoryCache → miss → PostgreSQL → populate cache → return
/// Write: update DB → Invalidate() → next read repopulates
///
/// Cache expiry is a safety-net fallback only.
/// Correctness relies on explicit invalidation after every mutation.
/// </summary>
internal sealed class BusinessSettingsService(
    AppDbContext db,
    IMemoryCache cache,
    IOptions<CacheOptions> cacheOptions,
    ILogger<BusinessSettingsService> logger) : IBusinessSettingsService
{
    public async Task<BusinessSettings?> GetAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(CacheKeys.BusinessSettings, out BusinessSettings? cached))
            return cached;

        var settings = await db.BusinessSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        if (settings is not null)
        {
            cache.Set(CacheKeys.BusinessSettings, settings, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow =
                    TimeSpan.FromMinutes(cacheOptions.Value.DefaultExpiryMinutes),
                Size = 1
            });
        }
        else
        {
            logger.LogWarning(
                "BusinessSettings row not found. " +
                "Bootstrap ({Key}) may not have been completed.",
                BusinessSettings.SingletonId);
        }

        return settings;
    }

    public void Invalidate()
    {
        cache.Remove(CacheKeys.BusinessSettings);
        logger.LogDebug("BusinessSettings cache invalidated.");
    }
}
