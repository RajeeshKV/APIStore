using FluentAssertions;
using KromicCommerce.Application.Abstractions.Catalog;
using KromicCommerce.Application.Abstractions.Store;
using KromicCommerce.Application.Caching;
using KromicCommerce.Infrastructure.Caching;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KromicCommerce.UnitTests.Infrastructure.Caching;

/// <summary>
/// A plan is only useful if translating it evicts the right things. These tests pin the
/// plan-to-mechanism mapping so a new projection cannot be added without deciding how it is
/// actually evicted.
/// </summary>
public sealed class CacheInvalidatorTests
{
    private readonly Mock<ICatalogCacheService> _catalogCache = new();
    private readonly Mock<IBusinessSettingsService> _settings = new();

    private CacheInvalidator CreateInvalidator() =>
        new(_catalogCache.Object, _settings.Object, NullLogger<CacheInvalidator>.Instance);

    private Task ApplyAsync(CacheInvalidationPlan plan) =>
        CreateInvalidator().ApplyAsync(plan).AsTask();

    [Fact]
    public async Task An_empty_plan_touches_nothing()
    {
        await ApplyAsync(CacheInvalidationPlan.Empty);

        _catalogCache.VerifyNoOtherCalls();
        _settings.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Parameterised_projections_evict_per_slug_and_per_product()
    {
        var slugA = "widget";
        var slugB = "gadget";
        var productId = Guid.NewGuid();

        await ApplyAsync(new CacheInvalidationPlan(
            CacheProjection.StorefrontProduct | CacheProjection.ProductReviews,
            [slugA, slugB],
            [productId]));

        _catalogCache.Verify(c => c.InvalidateStorefrontProduct(slugA), Times.Once);
        _catalogCache.Verify(c => c.InvalidateStorefrontProduct(slugB), Times.Once);
        _catalogCache.Verify(c => c.InvalidateProductReviews(productId), Times.Once);
    }

    [Fact]
    public async Task A_blank_slug_is_not_evicted()
    {
        // A product deleted in the same batch as its pages would otherwise produce empty keys.
        await ApplyAsync(new CacheInvalidationPlan(
            CacheProjection.StorefrontProduct, ["  ", null!], []));

        _catalogCache.Verify(c => c.InvalidateStorefrontProduct(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Global_projections_evict_their_own_cache_families()
    {
        await ApplyAsync(new CacheInvalidationPlan(
            CacheProjection.StorefrontFeatured |
            CacheProjection.StorefrontCategories |
            CacheProjection.StorefrontBrands |
            CacheProjection.AdminCategories |
            CacheProjection.AdminBrands |
            CacheProjection.StorefrontCarousel |
            CacheProjection.PublicPolicies));

        _catalogCache.Verify(c => c.InvalidateStorefrontFeatured(), Times.Once);
        _catalogCache.Verify(c => c.InvalidateStorefrontCategories(), Times.Once);
        _catalogCache.Verify(c => c.InvalidateStorefrontBrands(), Times.Once);
        _catalogCache.Verify(c => c.InvalidateCategories(), Times.Once);
        _catalogCache.Verify(c => c.InvalidateBrands(), Times.Once);
        _catalogCache.Verify(c => c.InvalidateCarousel(), Times.Once);
        _catalogCache.Verify(c => c.InvalidatePublicPolicies(), Times.Once);
    }

    [Fact]
    public async Task Orphaning_every_product_page_goes_through_the_catalog_epoch()
    {
        await ApplyAsync(new CacheInvalidationPlan(CacheProjection.AllStorefrontProducts));

        _catalogCache.Verify(c => c.InvalidateCatalogStructure(), Times.Once);
    }

    [Fact]
    public async Task A_shipping_change_evicts_the_settings_object_and_its_delivery_dependents()
    {
        await ApplyAsync(new CacheInvalidationPlan(
            CacheProjection.BusinessSettings | CacheProjection.ShippingConfiguration));

        // InvalidateShipping already drops the settings entry, so calling both would be redundant.
        _settings.Verify(s => s.InvalidateShipping(), Times.Once);
        _settings.Verify(s => s.Invalidate(), Times.Never);
    }

    [Fact]
    public async Task A_non_shipping_settings_change_evicts_only_the_settings_object()
    {
        // A tax edit must not orphan every cached product page; nothing on a product page
        // derives from TaxSettings.
        await ApplyAsync(new CacheInvalidationPlan(CacheProjection.BusinessSettings));

        _settings.Verify(s => s.Invalidate(), Times.Once);
        _settings.Verify(s => s.InvalidateShipping(), Times.Never);
        _catalogCache.Verify(c => c.InvalidateCatalogStructure(), Times.Never);
    }

    [Fact]
    public async Task An_unknown_projection_bit_evicts_nothing_rather_than_throwing()
    {
        // Forward compatibility: a newer plan applied by an older service must not crash a save.
        await ApplyAsync(new CacheInvalidationPlan((CacheProjection)(1 << 30)));

        _catalogCache.VerifyNoOtherCalls();
        _settings.VerifyNoOtherCalls();
    }
}

/// <summary>
/// The plan builder is shared by the interceptor, so its normalisation rules are worth pinning.
/// </summary>
public sealed class CacheInvalidationPlanBuilderTests
{
    [Fact]
    public void Slugs_are_normalised_so_a_case_or_whitespace_difference_still_hits_the_same_entry()
    {
        var builder = new CacheInvalidationPlanBuilder();
        builder.AddSlug("  Blue-Widget ");
        builder.AddSlug("blue-widget");

        var plan = builder.Build();
        plan.ProductSlugs.Should().ContainSingle().Which.Should().Be("blue-widget");
    }

    [Fact]
    public void Blank_slugs_are_discarded()
    {
        var builder = new CacheInvalidationPlanBuilder();
        builder.AddSlug(null);
        builder.AddSlug("");
        builder.AddSlug("   ");

        builder.Build().Should().BeSameAs(CacheInvalidationPlan.Empty);
    }

    [Fact]
    public void Duplicate_product_ids_collapse()
    {
        var builder = new CacheInvalidationPlanBuilder();
        var id = Guid.NewGuid();
        builder.AddProductId(id);
        builder.AddProductId(id);

        builder.Build().ProductIds.Should().ContainSingle().Which.Should().Be(id);
    }

    [Fact]
    public void Projections_are_unioned_rather_than_replaced()
    {
        var builder = new CacheInvalidationPlanBuilder { Seed = CacheProjection.StorefrontProduct };
        builder.Add(CacheProjection.StorefrontFeatured);
        builder.Add(CacheProjection.StorefrontFeatured);

        var plan = builder.Build();
        plan.Projections.Should().HaveFlag(CacheProjection.StorefrontProduct);
        plan.Projections.Should().HaveFlag(CacheProjection.StorefrontFeatured);
    }

    [Fact]
    public void An_empty_builder_reuses_the_shared_empty_plan()
    {
        new CacheInvalidationPlanBuilder().Build().Should().BeSameAs(CacheInvalidationPlan.Empty);
    }
}