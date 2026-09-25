namespace KromicCommerce.Application.Options;

/// <summary>
/// Cache TTL settings consumed by Application catalog query handlers.
/// Infrastructure binds the values from CacheOptions and registers this type,
/// keeping Application free of any Infrastructure.Configuration dependency.
/// </summary>
public sealed class CatalogCacheOptions
{
    public int DefaultExpiryMinutes { get; set; } = 60;
    public int ShortExpiryMinutes { get; set; } = 5;
}
