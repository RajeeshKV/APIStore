using KromicCommerce.Application.Caching;
using KromicCommerce.Infrastructure.Caching;
using KromicCommerce.Infrastructure.Catalog;
using Microsoft.Extensions.Caching.Memory;

namespace KromicCommerce.UnitTests.Infrastructure.Caching;

/// <summary>
/// Verifies the dependency direction of catalog cache invalidation against a real
/// <see cref="IMemoryCache"/>.
///
/// The existing <c>StorefrontCacheInvalidationTests</c> asserts that a <em>product</em> mutation
/// fans out to brands and categories, because those projections embed a product count. These
/// tests cover the opposite direction — a <em>brand or category</em> mutation fanning out to the
/// product projections, because those embed the brand/category name.
///
/// Invalidations that cannot enumerate keys use an epoch: the entry stays physically in the cache
/// but becomes unreachable through the current epoch. These tests therefore assert
/// <b>reachability</b>, which is what a stale read actually depends on, not physical presence.
/// </summary>
public sealed class CatalogCacheServiceGraphTests : IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 1024 });
    private readonly CatalogCacheService _service;

    private const string Slug = "air-max-90";

    public CatalogCacheServiceGraphTests() => _service = new CatalogCacheService(_cache);

    public void Dispose() => _cache.Dispose();

    /// <summary>The key the storefront handler would use right now.</summary>
    private string CurrentProductKey(string slug) =>
        CatalogCacheKeys.CatalogScopedStorefrontProduct(
            slug, CatalogCacheKeys.CurrentCatalogEpoch(_cache), _service.GetShippingEpoch());

    private bool Reachable(string slug) => _cache.TryGetValue(CurrentProductKey(slug), out _);

    private void SeedProductPage(string slug = Slug)
        => _cache.Set(CurrentProductKey(slug), "page", new MemoryCacheEntryOptions { Size = 1 });

    private void SeedFeatured(int limit = 8)
        => _cache.Set(CacheKeys.StorefrontFeatured(limit), "featured",
            new MemoryCacheEntryOptions { Size = 1 });

    // -----------------------------------------------------------------------
    // Upward direction: product → brand/category counts (was already implemented)
    // -----------------------------------------------------------------------

    [Fact]
    public void A_product_change_evicts_the_brand_and_category_count_projections()
    {
        _cache.Set(CacheKeys.StorefrontBrands, "brands", new MemoryCacheEntryOptions { Size = 1 });
        _cache.Set(CacheKeys.StorefrontCategories, "categories", new MemoryCacheEntryOptions { Size = 1 });
        SeedProductPage();
        SeedFeatured();

        _service.InvalidateProductGraph(Guid.NewGuid(), Slug);

        _cache.TryGetValue(CacheKeys.StorefrontBrands, out _).Should().BeFalse();
        _cache.TryGetValue(CacheKeys.StorefrontCategories, out _).Should().BeFalse();
        _cache.TryGetValue(CacheKeys.StorefrontFeatured(8), out _).Should().BeFalse();
        // The graph overload knows the slug, so the product page is removed outright.
        Reachable(Slug).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Downward direction: brand/category → product pages (the gap this audit found)
    // -----------------------------------------------------------------------

    [Fact]
    public void A_brand_change_makes_every_product_page_unreachable()
    {
        // StorefrontProductResponse carries BrandName/BrandSlug. Before the catalog epoch existed,
        // renaming a brand left every cached product page serving the old name until expiry.
        SeedProductPage();
        SeedFeatured();

        _service.InvalidateBrandGraph();

        Reachable(Slug).Should().BeFalse(
            "StorefrontProductResponse embeds BrandName, so renaming a brand must orphan product pages");
    }

    [Fact]
    public void A_category_change_makes_every_product_page_unreachable()
    {
        // StorefrontProductResponse carries CategoryName/CategorySlug.
        SeedProductPage();

        _service.InvalidateCategoryGraph();

        Reachable(Slug).Should().BeFalse(
            "StorefrontProductResponse embeds CategoryName, so renaming a category must orphan product pages");
    }

    [Fact]
    public void A_brand_change_evicts_the_featured_list_which_embeds_the_brand_name()
    {
        SeedFeatured(4);
        SeedFeatured(12);

        _service.InvalidateBrandGraph();

        _cache.TryGetValue(CacheKeys.StorefrontFeatured(4), out _).Should().BeFalse();
        _cache.TryGetValue(CacheKeys.StorefrontFeatured(12), out _).Should().BeFalse();
    }

    [Fact]
    public void A_category_change_evicts_the_featured_list_which_embeds_the_category_name()
    {
        SeedFeatured(6);

        _service.InvalidateCategoryGraph();

        _cache.TryGetValue(CacheKeys.StorefrontFeatured(6), out _).Should().BeFalse();
    }

    [Fact]
    public void A_brand_change_orphans_pages_for_products_that_merely_belong_to_the_brand()
    {
        // The blast radius cannot be narrowed to the brand's own products without a database query,
        // so an unrelated product's page is orphaned too. Documented as the accepted cost of not
        // being able to enumerate keys.
        SeedProductPage("some-other-product");

        _service.InvalidateBrandGraph();

        Reachable("some-other-product").Should().BeFalse();
    }

    [Fact]
    public void The_catalog_epoch_advances_once_per_brand_change()
    {
        var before = _service.GetCatalogEpoch();

        _service.InvalidateBrandGraph();

        _service.GetCatalogEpoch().Should().Be(before + 1);
    }

    [Fact]
    public void A_product_page_is_reachable_again_once_it_is_repopulated_under_the_new_epoch()
    {
        SeedProductPage();
        _service.InvalidateBrandGraph();
        Reachable(Slug).Should().BeFalse();

        // Simulates the next storefront read repopulating under the current epoch.
        _cache.Set(CurrentProductKey(Slug), "fresh page", new MemoryCacheEntryOptions { Size = 1 });

        Reachable(Slug).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Shipping epoch must stay independent of the catalog epoch
    // -----------------------------------------------------------------------

    [Fact]
    public void A_shipping_change_orphans_product_pages_without_advancing_the_catalog_epoch()
    {
        SeedProductPage();
        var catalogEpochBefore = _service.GetCatalogEpoch();

        _service.InvalidateShippingConfiguration();

        Reachable(Slug).Should().BeFalse();
        _service.GetCatalogEpoch().Should().Be(catalogEpochBefore);
    }

    [Fact]
    public void A_product_change_does_not_orphan_other_products_pages()
    {
        // The per-slug overload knows exactly which page to evict, so it must not pay the
        // broad-epoch cost that a brand change does.
        SeedProductPage("product-a");
        SeedProductPage("product-b");

        _service.InvalidateProductGraph(Guid.NewGuid(), "product-a");

        Reachable("product-a").Should().BeFalse();
        Reachable("product-b").Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Guard rails: the admin lists must still be cleared
    // -----------------------------------------------------------------------

    [Fact]
    public void A_brand_change_still_clears_the_admin_brand_lists()
    {
        _cache.Set(CatalogCacheKeys.AdminBrands(true), "a", new MemoryCacheEntryOptions { Size = 1 });
        _cache.Set(CatalogCacheKeys.AdminBrands(false), "b", new MemoryCacheEntryOptions { Size = 1 });
        _cache.Set(CacheKeys.AllBrands, "c", new MemoryCacheEntryOptions { Size = 1 });

        _service.InvalidateBrandGraph();

        _cache.TryGetValue(CatalogCacheKeys.AdminBrands(true), out _).Should().BeFalse();
        _cache.TryGetValue(CatalogCacheKeys.AdminBrands(false), out _).Should().BeFalse();
        _cache.TryGetValue(CacheKeys.AllBrands, out _).Should().BeFalse();
    }

    [Fact]
    public void A_category_change_still_clears_the_admin_category_lists()
    {
        _cache.Set(CatalogCacheKeys.AdminCategories(true), "a", new MemoryCacheEntryOptions { Size = 1 });
        _cache.Set(CatalogCacheKeys.AdminCategories(false), "b", new MemoryCacheEntryOptions { Size = 1 });
        _cache.Set(CacheKeys.AllCategories, "c", new MemoryCacheEntryOptions { Size = 1 });

        _service.InvalidateCategoryGraph();

        _cache.TryGetValue(CatalogCacheKeys.AdminCategories(true), out _).Should().BeFalse();
        _cache.TryGetValue(CatalogCacheKeys.AdminCategories(false), out _).Should().BeFalse();
        _cache.TryGetValue(CacheKeys.AllCategories, out _).Should().BeFalse();
    }
}
