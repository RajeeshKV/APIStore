using KromicCommerce.Application.Caching;

namespace KromicCommerce.Infrastructure.Caching;

/// <summary>
/// Infrastructure cache key definitions.
/// Delegates to <see cref="CatalogCacheKeys"/> for keys shared with Application
/// so the strings stay in one place with no upward dependency violation.
/// </summary>
public static class CacheKeys
{
    public const string BusinessSettings = CatalogCacheKeys.BusinessSettings;

    // -----------------------------------------------------------------------
    // Admin catalog (delegate to Application constants — single source)
    // -----------------------------------------------------------------------
    public const string AllCategories = CatalogCacheKeys.AllCategories;
    public const string AllBrands     = CatalogCacheKeys.AllBrands;
    public const string AllProducts   = CatalogCacheKeys.AllProducts;

    public static string Product(Guid id) => CatalogCacheKeys.AdminProduct(id);

    // -----------------------------------------------------------------------
    // Storefront catalog
    // -----------------------------------------------------------------------
    public const string StorefrontCategories = CatalogCacheKeys.StorefrontCategories;
    public const string StorefrontBrands     = CatalogCacheKeys.StorefrontBrands;

    /// <summary>
    /// Base prefix for featured-product entries. Entries are keyed per limit; invalidation
    /// removes each configured limit in turn because IMemoryCache cannot prefix-delete.
    /// </summary>
    public const string StorefrontFeaturedPrefix = CatalogCacheKeys.StorefrontFeaturedPrefix;

    public static string StorefrontFeatured(int limit) =>
        CatalogCacheKeys.StorefrontFeaturedProducts(limit);

    public static string StorefrontProduct(string slug) =>
        CatalogCacheKeys.StorefrontProduct(slug);

    // Store policies
    public const string PublicPolicies = CatalogCacheKeys.PublicPolicies;
}
