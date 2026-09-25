namespace KromicCommerce.Application.Abstractions.Store;

/// <summary>
/// Read/write access to business settings with integrated cache management.
/// The read path goes through IMemoryCache (cache-aside).
/// Every mutation must call InvalidateAsync() after committing to the database
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
}
