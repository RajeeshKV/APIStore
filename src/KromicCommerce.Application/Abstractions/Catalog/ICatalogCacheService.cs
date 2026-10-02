namespace KromicCommerce.Application.Abstractions.Catalog;

/// <summary>
/// Typed catalog cache invalidation contract.
///
/// Every mutation handler must call the relevant method after SaveChangesAsync.
/// Invalidation happens only after successful persistence — never before.
///
/// Dependency-aware invalidation
/// ------------------------------
/// Cached projections are not independent: the storefront brand and category lists embed a
/// ProductCount computed from the products table, and the featured list embeds per-product
/// stock. A single product mutation therefore invalidates more than the product's own key.
/// Callers should prefer the coarse-grained <c>*Graph</c> methods over assembling several
/// fine-grained calls by hand — that is what keeps dependent projections from going stale.
/// </summary>
public interface ICatalogCacheService
{
    // -----------------------------------------------------------------------
    // Admin catalog cache (legacy list views)
    // -----------------------------------------------------------------------
    void InvalidateCategories();
    void InvalidateBrands();
    void InvalidateProducts();
    void InvalidateProduct(Guid productId);

    // -----------------------------------------------------------------------
    // Storefront cache (public-facing, richer DTOs)
    // -----------------------------------------------------------------------
    void InvalidateStorefrontCategories();
    void InvalidateStorefrontBrands();
    void InvalidateStorefrontProduct(string slug);
    void InvalidateStorefrontFeatured();

    /// <summary>
    /// Invalidates the public Home page carousel.
    /// Call after any carousel create, update, delete, image change, or enable/disable: the
    /// storefront projection embeds visibility, ordering and the image, so all of those changes
    /// alter what the next public read should return.
    /// </summary>
    void InvalidateCarousel();

    /// <summary>
    /// Invalidates the cached list of published store policies shown to customers.
    /// </summary>
    void InvalidatePublicPolicies();

    /// <summary>
    /// Invalidates every cached public review page for one product.
    /// Call after any review write that can change what the public list should return: submit,
    /// edit, delete, moderate, or a helpful vote (which changes the "most helpful" ordering).
    ///
    /// Implemented by bumping a per-product epoch rather than enumerating keys, because
    /// IMemoryCache cannot prefix-delete and a popular product can have many cached pages
    /// across every sort, filter and page combination.
    /// </summary>
    void InvalidateProductReviews(Guid productId);

    // -----------------------------------------------------------------------
    // Dependency-aware (graph) invalidation
    // -----------------------------------------------------------------------

    /// <summary>
    /// Invalidates every projection that depends on the product table: the product
    /// projections, the featured list, and the brand/category lists that embed ProductCount.
    /// Call after any product create/update/status change/delete.
    /// </summary>
    void InvalidateProductGraph();

    /// <summary>
    /// Invalidates a single product and every projection that depends on it:
    /// the product's own entries, the featured list, and the brand/category ProductCounts.
    /// </summary>
    void InvalidateProductGraph(Guid productId, string? slug);

    /// <summary>
    /// Invalidates availability-dependent projections for one product: the product page
    /// and the featured list. Use after stock mutations, which never change counts.
    /// </summary>
    void InvalidateStockGraph(string? slug);

    /// <summary>Invalidates both the admin and storefront brand projections.</summary>
    void InvalidateBrandGraph();

    /// <summary>Invalidates both the admin and storefront category projections.</summary>
    void InvalidateCategoryGraph();

    /// <summary>
    /// Orphans every cached storefront product page and featured list.
    ///
    /// Use for mutations whose blast radius spans products that cannot be enumerated from the
    /// cache alone — a brand or category rename or deactivation, since product pages embed the
    /// brand and category names. Evicting only the brand/category lists would leave every cached
    /// product page showing the old name until its absolute expiry.
    ///
    /// Achieved by bumping the catalog epoch (see <c>CatalogCacheKeys.CatalogEpochKey</c>), so
    /// cost is O(1) rather than proportional to the number of cached products. Already called by
    /// <c>InvalidateBrandGraph</c> and <c>InvalidateCategoryGraph</c>; call it directly only when
    /// product pages must be invalidated without touching the brand or category lists themselves.
    /// </summary>
    void InvalidateCatalogStructure();

    /// <summary>Current catalog epoch, used to build catalog-scoped keys.</summary>
    int GetCatalogEpoch();

    /// <summary>
    /// Invalidates every cache entry that embeds data derived from the shipping /
    /// cash-on-delivery configuration.
    ///
    /// Storefront product detail responses embed a delivery estimate computed from
    /// BusinessSettings.Delivery. IMemoryCache cannot prefix-delete, so the entries are
    /// addressed through a shipping epoch (see <c>CatalogCacheKeys.ShippingEpochKey</c>);
    /// bumping it makes every previously written delivery-scoped entry unreachable.
    /// The cached BusinessSettings object itself is evicted separately by
    /// <c>IBusinessSettingsService.InvalidateShipping()</c>.
    /// </summary>
    void InvalidateShippingConfiguration();

    /// <summary>Current shipping-configuration epoch, used to build delivery-scoped keys.</summary>
    int GetShippingEpoch();
}
