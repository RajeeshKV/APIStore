namespace KromicCommerce.Application.Abstractions.Catalog;

/// <summary>
/// Typed catalog cache invalidation contract.
/// Every mutation handler must call the relevant method after SaveChangesAsync.
/// Invalidation happens only after successful persistence — never before.
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
}
