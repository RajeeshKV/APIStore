namespace KromicCommerce.Application.Abstractions.Store;

/// <summary>
/// Evicts the cached <c>BusinessSettings</c> entry and the cache entries that embed data derived
/// from it.
/// </summary>
/// <remarks>
/// <para>
/// This exists so cache invalidation can be reached without going through
/// <see cref="IBusinessSettingsService"/>, which reads and writes the settings row through
/// <c>AppDbContext</c>. <c>AppDbContext</c> constructs the EF save interceptor that drives
/// <see cref="Catalog.ICacheInvalidator"/>, so an invalidator that depended on
/// <see cref="IBusinessSettingsService"/> closed the loop
/// <c>AppDbContext → CacheInvalidationInterceptor → CacheInvalidator →
/// BusinessSettingsService → AppDbContext</c>. The container could not satisfy that: resolving
/// <c>AppDbContext</c> deadlocked before Kestrel ever bound, which presented as a deploy that
/// timed out with "no open ports".
/// </para>
///
/// <para>
/// The dependency now points the other way. <see cref="IBusinessSettingsService"/> depends on
/// this, never the reverse, and every implementation of this abstraction is restricted to cache
/// infrastructure — <c>IMemoryCache</c> and <c>ICatalogCacheService</c>. There is deliberately no
/// read, query or persistence on this interface: adding one would be the first step back towards
/// the cycle.
/// </para>
/// </remarks>
public interface IBusinessSettingsCacheInvalidator
{
    /// <summary>Evicts the cached BusinessSettings entry.</summary>
    void Invalidate();

    /// <summary>
    /// Evicts the cached BusinessSettings entry <em>and</em> the cache entries that embed
    /// shipping / cash-on-delivery derived data — storefront product pages, which carry a
    /// delivery estimate computed from the delivery configuration.
    /// </summary>
    void InvalidateShipping();
}
