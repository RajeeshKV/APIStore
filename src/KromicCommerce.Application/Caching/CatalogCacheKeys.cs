namespace KromicCommerce.Application.Caching;

/// <summary>
/// Catalog cache key constants shared by Application handlers.
/// Infrastructure mirrors these in CacheKeys.cs (which may add infrastructure-only keys).
/// Having the constants in Application avoids an upward dependency on Infrastructure.
/// </summary>
public static class CatalogCacheKeys
{
    // Admin listing caches
    public const string AllCategories = "catalog:categories:all";
    public const string AllBrands     = "catalog:brands:all";
    public const string AllProducts   = "catalog:products:all";

    // Storefront caches
    public const string StorefrontCategories = "storefront:categories:all";
    public const string StorefrontBrands     = "storefront:brands:all";
    public const string StorefrontFeatured   = "storefront:products:featured";

    public static string StorefrontProduct(string slug) =>
        $"storefront:product:{slug.ToLowerInvariant()}";

    public static string AdminProduct(Guid id) => $"catalog:product:{id}";

    // Store configuration caches
    public const string BusinessSettings = "store:business_settings";
    public const string PublicPolicies   = "store:policies:public";
}
