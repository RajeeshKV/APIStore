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
/// <remarks>
/// The service depends on <see cref="IBusinessSettingsCacheInvalidator"/>, never the other way
/// around. The EF save interceptor that also evicts the settings cache is constructed while
/// <c>AppDbContext</c> is being built, so it cannot reach a dependency that needs the context —
/// see <see cref="IBusinessSettingsCacheInvalidator"/> for the cycle that motivated the split.
/// </remarks>
internal sealed class BusinessSettingsService(
    AppDbContext db,
    IMemoryCache cache,
    IBusinessSettingsCacheInvalidator settingsCache,
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

    public void Invalidate() => settingsCache.Invalidate();

    public void InvalidateShipping() => settingsCache.InvalidateShipping();
}
