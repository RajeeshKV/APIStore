namespace KromicCommerce.Infrastructure.Configuration;

public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    /// <summary>Default absolute expiry for catalog/configuration cache entries (minutes). Default: 60.</summary>
    public int DefaultExpiryMinutes { get; init; } = 60;

    /// <summary>Short-lived cache entries (e.g. product availability) expiry in minutes. Default: 5.</summary>
    public int ShortExpiryMinutes { get; init; } = 5;

    /// <summary>IMemoryCache size limit (entry count units, not bytes). Default: 1024.</summary>
    public long SizeLimit { get; init; } = 1024;
}
