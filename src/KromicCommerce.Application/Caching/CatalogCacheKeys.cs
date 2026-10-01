namespace KromicCommerce.Application.Caching;

/// <summary>
/// Catalog cache key constants shared by Application handlers.
/// Infrastructure mirrors these in CacheKeys.cs (which may add infrastructure-only keys).
/// Having the constants in Application avoids an upward dependency on Infrastructure.
///
/// Key conventions
///   <c>catalog:&lt;entity&gt;:&lt;variant&gt;</c>      — admin projections
///   <c>storefront:&lt;entity&gt;:&lt;variant&gt;</c>  — public projections
///   <c>store:&lt;entity&gt;:&lt;variant&gt;</c>       — store configuration
///
/// Every key that varies with a request parameter MUST encode that parameter, otherwise a
/// cached entry produced for one query is served for a different query (see
/// <see cref="AdminBrands"/>, <see cref="AdminCategories"/>, <see cref="DeliveryScopedStorefrontProduct"/>).
/// </summary>
public static class CatalogCacheKeys
{
    // -----------------------------------------------------------------------
    // Admin listing caches
    // -----------------------------------------------------------------------
    public const string AllCategories = "catalog:categories:all";
    public const string AllBrands     = "catalog:brands:all";
    public const string AllProducts   = "catalog:products:all";

    // -----------------------------------------------------------------------
    // Storefront caches
    // -----------------------------------------------------------------------
    public const string StorefrontCategories = "storefront:categories:all";
    public const string StorefrontBrands     = "storefront:brands:all";
    public const string StorefrontFeaturedPrefix = "storefront:products:featured";

    /// <summary>
    /// Public Home page carousel. One key: the storefront asks for every visible slide in one
    /// call, so there is no query parameter to encode.
    /// </summary>
    public const string StorefrontCarousel = "storefront:carousel:all";

    /// <summary>
    /// Featured-products key. The limit is part of the key because it is baked into the
    /// cached result; without it a limit=4 request would satisfy a later limit=50 request.
    /// <see cref="ICatalogCacheService.InvalidateStorefrontFeatured"/> evicts every limit.
    /// </summary>
    public static string StorefrontFeaturedProducts(int limit) => $"{StorefrontFeaturedPrefix}:{limit}";

    // -----------------------------------------------------------------------
    // Store configuration caches
    // -----------------------------------------------------------------------
    public const string BusinessSettings = "store:business_settings";
    public const string PublicPolicies   = "store:policies:public";

    // -----------------------------------------------------------------------
    // Shipping / COD configuration epoch
    //
    // Storefront product detail responses embed a DeliveryEstimateDto derived from
    // BusinessSettings.Delivery (processing / min / max delivery days). IMemoryCache cannot
    // enumerate or prefix-delete keys, so delivery-scoped entries are addressed through a
    // monotonically increasing epoch. Bumping the epoch orphans every previously written
    // delivery-scoped key, which makes the next read a guaranteed miss without ever having
    // to know which slugs are currently cached.
    //
    // Orphaned entries are reclaimed by the cache SizeLimit / absolute expiry.
    // -----------------------------------------------------------------------
    public const string ShippingEpoch = "store:shipping_epoch";

    /// <summary>Key holding the current shipping/COD configuration epoch.</summary>
    public const string ShippingEpochKey = ShippingEpoch;

    public static string StorefrontProduct(string slug) =>
        $"{StorefrontProductPrefix}{NormaliseSlug(slug)}";

    public const string StorefrontProductPrefix = "storefront:product:";

    /// <summary>
    /// Storefront product detail key that is scoped to a shipping-configuration epoch.
    /// Use together with <see cref="ShippingEpochKey"/> so that a shipping or COD change
    /// invalidates every cached product page without enumerating cache keys.
    /// </summary>
    public static string DeliveryScopedStorefrontProduct(string slug, int shippingEpoch) =>
        $"{StorefrontProductPrefix}{NormaliseSlug(slug)}|e{shippingEpoch}";

    public static string AdminProduct(Guid id) => $"catalog:product:{id}";

    /// <summary>
    /// Admin brand list key. <paramref name="activeOnly"/> is part of the key because the
    /// handler filters after the cache lookup — omitting it would let a non-filtered read
    /// poison the entry for a filtered read and leak inactive brands to admins.
    /// </summary>
    public static string AdminBrands(bool activeOnly) => $"{AllBrands}:{(activeOnly ? "active" : "all")}";

    /// <inheritdoc cref="AdminBrands"/>
    public static string AdminCategories(bool activeOnly) => $"{AllCategories}:{(activeOnly ? "active" : "all")}";

    /// <summary>Normalises a slug to its canonical cached form (trimmed, lower-case).</summary>
    public static string NormaliseSlug(string slug) => slug.Trim().ToLowerInvariant();
}
