namespace KromicCommerce.Application.Caching;

/// <summary>
/// A cached projection that can be evicted.
///
/// Flags rather than a list because a single entity write almost always dirties more than one
/// projection — a product edit changes the product page, the featured list, and the ProductCount
/// embedded in both the brand and category lists. Callers OR the values together and evict once.
/// </summary>
/// <remarks>
/// Every value here must correspond to a real cache entry family. The set is deliberately small:
/// an entity that no projection embeds must NOT appear, because registering it would evict
/// caches for no reason and cost cache hit rate on every write.
/// </remarks>
[Flags]
public enum CacheProjection
{
    None = 0,

    /// <summary>Storefront product detail page for specific slugs (see the plan's slug set).</summary>
    StorefrontProduct = 1 << 0,

    /// <summary>
    /// Every cached storefront product page, regardless of slug.
    ///
    /// Product pages are addressed through the catalog epoch rather than one key per slug, so
    /// orphaning all of them is O(1). Used when a change alters data every product page embeds —
    /// today only the currency code, which comes from BusinessSettings.
    /// </summary>
    AllStorefrontProducts = 1 << 1,

    /// <summary>Storefront "featured products" list, keyed per requested limit.</summary>
    StorefrontFeatured = 1 << 2,

    /// <summary>Storefront category list, which embeds an active ProductCount.</summary>
    StorefrontCategories = 1 << 3,

    /// <summary>Storefront brand list, which embeds an active ProductCount.</summary>
    StorefrontBrands = 1 << 4,

    /// <summary>Admin category list (active-only and all variants).</summary>
    AdminCategories = 1 << 5,

    /// <summary>Admin brand list (active-only and all variants).</summary>
    AdminBrands = 1 << 6,

    /// <summary>Public home-page carousel.</summary>
    StorefrontCarousel = 1 << 7,

    /// <summary>Published store policies shown to customers.</summary>
    PublicPolicies = 1 << 8,

    /// <summary>The cached BusinessSettings singleton row.</summary>
    BusinessSettings = 1 << 9,

    /// <summary>
    /// Everything derived from the shipping / cash-on-delivery configuration, which storefront
    /// product pages embed as a delivery estimate.
    /// </summary>
    ShippingConfiguration = 1 << 10,

    /// <summary>Public review pages for specific products (see the plan's product-id set).</summary>
    ProductReviews = 1 << 11,
}