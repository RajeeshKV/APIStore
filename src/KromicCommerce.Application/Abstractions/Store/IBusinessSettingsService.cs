namespace KromicCommerce.Application.Abstractions.Store;

/// <summary>
/// Read/write access to business settings with integrated cache management.
/// The read path goes through IMemoryCache (cache-aside).
/// Every mutation must call Invalidate() after committing to the database
/// so the next read repopulates from PostgreSQL.
/// </summary>
public interface IBusinessSettingsService
{
    /// <summary>Returns the singleton BusinessSettings, from cache when available.</summary>
    Task<BusinessSettings?> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Evicts the cached BusinessSettings entry.
    /// Must be called after every successful admin mutation.
    /// </summary>
    void Invalidate();

    /// <summary>
    /// Evicts the cached BusinessSettings entry and every downstream cache entry that embeds
    /// shipping / cash-on-delivery derived data.
    ///
    /// Use this — not <see cref="Invalidate"/> — whenever BusinessSettings.Delivery changes
    /// (COD enabled/disabled, COD fee, flat fee, free-shipping threshold, delivery-day
    /// estimates), so that checkout pricing and storefront delivery estimates can never be
    /// served from a stale cache.
    /// </summary>
    void InvalidateShipping();
}
