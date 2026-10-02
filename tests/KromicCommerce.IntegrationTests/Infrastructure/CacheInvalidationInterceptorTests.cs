using FluentAssertions;
using KromicCommerce.Application.Abstractions.Catalog;
using KromicCommerce.Domain.Cart;
using KromicCommerce.Domain.Catalog;
using KromicCommerce.Domain.Outbox;
using KromicCommerce.Domain.Store;
using KromicCommerce.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace KromicCommerce.IntegrationTests.Infrastructure;

/// <summary>
/// Proves the global invalidation layer end to end against real PostgreSQL and a real
/// <see cref="IMemoryCache"/>.
///
/// <para>
/// The point is that these mutations invalidate because they were saved — not because a handler
/// remembered to call something. Each test writes directly through the context, exactly as a future
/// handler would, and asserts the right projections went stale.
/// </para>
/// </summary>
[Collection("Database")]
public sealed class CacheInvalidationInterceptorTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    private readonly MemoryCache _memoryCache = new(new MemoryCacheOptions { SizeLimit = 4096 });

    // -----------------------------------------------------------------------
    // Catalog
    // -----------------------------------------------------------------------

    [SkippableFact]
    public async Task Saving_a_product_invalidates_the_product_page_the_featured_list_and_the_counts()
    {
        await using var ctx = Db.CreateCacheObservedDbContext(_memoryCache, out var recorder);

        ctx.Products.Add(Product.Create(
            "Cache Widget", $"cache-widget-{Guid.NewGuid():N}", $"SKU-{Guid.NewGuid():N}", 10m, null, null));
        await ctx.SaveChangesAsync();

        recorder.Called(nameof(ICatalogCacheService.InvalidateStorefrontProduct))
            .Should().BeTrue("the product's own page may be cached under a slug from a previous edit");
        recorder.Called(nameof(ICatalogCacheService.InvalidateStorefrontFeatured))
            .Should().BeTrue("a new product can be featured");
        // The trap this layer exists to catch: the storefront lists embed an active ProductCount,
        // so creating a product moves those counts.
        recorder.Called(nameof(ICatalogCacheService.InvalidateStorefrontCategories))
            .Should().BeTrue();
        recorder.Called(nameof(ICatalogCacheService.InvalidateStorefrontBrands))
            .Should().BeTrue();
    }

    [SkippableFact]
    public async Task A_stock_change_invalidates_the_product_page_under_that_products_slug()
    {
        await using var ctx = Db.CreateCacheObservedDbContext(_memoryCache, out _);
        var product = Product.Create(
            "Stocked", $"stocked-{Guid.NewGuid():N}", $"SKU-{Guid.NewGuid():N}", 10m, null, null);
        ctx.Products.Add(product);
        await ctx.SaveChangesAsync();

        // A separate context and recorder, so this save's effect is isolated from the seed above.
        await using var stockCtx = Db.CreateCacheObservedDbContext(_memoryCache, out var recorder);
        var target = await stockCtx.Products.FirstAsync(p => p.Id == product.Id);

        stockCtx.InventoryItems.Add(InventoryItem.Create(target.Id, null, onHand: 7));
        await stockCtx.SaveChangesAsync();

        // Stock carries only a product id, while the cached page is keyed by slug, so resolving the
        // slug is the interceptor's job rather than the caller's.
        recorder.Called(nameof(ICatalogCacheService.InvalidateStorefrontProduct))
            .Should().BeTrue($"the cached page is keyed by slug, and this product's is '{product.Slug}'");
        recorder.Called(nameof(ICatalogCacheService.InvalidateStorefrontFeatured))
            .Should().BeTrue("the featured list embeds per-product availability");
    }

    [SkippableFact]
    public async Task A_stock_change_does_not_evict_brand_or_category_counts()
    {
        await using var ctx = Db.CreateCacheObservedDbContext(_memoryCache, out _);
        var product = Product.Create(
            "Counts", $"counts-{Guid.NewGuid():N}", $"SKU-{Guid.NewGuid():N}", 10m, null, null);
        ctx.Products.Add(product);
        await ctx.SaveChangesAsync();

        await using var stockCtx = Db.CreateCacheObservedDbContext(_memoryCache, out var recorder);
        var target = await stockCtx.Products.FirstAsync(p => p.Id == product.Id);
        stockCtx.InventoryItems.Add(InventoryItem.Create(target.Id, null, onHand: 3));
        await stockCtx.SaveChangesAsync();

        // Over-invalidating here would cost cache hit rate on every checkout for no correctness gain.
        recorder.Called(nameof(ICatalogCacheService.InvalidateStorefrontCategories))
            .Should().BeFalse("a stock change moves no product counts");
        recorder.Called(nameof(ICatalogCacheService.InvalidateStorefrontBrands))
            .Should().BeFalse("a stock change moves no product counts");
    }

    [SkippableFact]
    public async Task A_category_rename_orphans_every_cached_product_page()
    {
        await using var ctx = Db.CreateCacheObservedDbContext(_memoryCache, out _);
        var category = Category.Create("Old Name", $"cat-{Guid.NewGuid():N}", null, null);
        ctx.Categories.Add(category);
        await ctx.SaveChangesAsync();

        await using var renameCtx = Db.CreateCacheObservedDbContext(_memoryCache, out var recorder);
        var target = await renameCtx.Categories.FirstAsync(c => c.Id == category.Id);
        target.Update("New Name", target.Slug, target.Description, target.ParentCategoryId, target.SortOrder);
        await renameCtx.SaveChangesAsync();

        recorder.Called(nameof(ICatalogCacheService.InvalidateCatalogStructure))
            .Should().BeTrue("every product page embeds the category name and slug");
    }

    // -----------------------------------------------------------------------
    // Settings
    // -----------------------------------------------------------------------

    [SkippableFact]
    public async Task A_delivery_change_bumps_the_shipping_epoch()
    {
        await using var ctx = Db.CreateCacheObservedDbContext(_memoryCache, out var recorder);

        var settings = await LoadSettingsAsync(ctx);
        settings.SetCodEnabled(!settings.Delivery.CodEnabled);
        await ctx.SaveChangesAsync();

// Storefront product pages embed a delivery estimate computed from Delivery. The shipping
        // epoch is bumped via IBusinessSettingsService.InvalidateShipping, which owns the settings
        // object and its delivery-scoped dependents.
        recorder.Called("BusinessSettings:InvalidateShipping")
            .Should().BeTrue();
    }

    [SkippableFact]
    public async Task A_settings_change_nothing_cached_embeds_stays_narrow()
    {
        await using var ctx = Db.CreateCacheObservedDbContext(_memoryCache, out var recorder);

        // Opening/closing the store is a settings change, but neither the shipping estimate nor
        // the currency on a product page depends on it.
        var settings = await LoadSettingsAsync(ctx);
        settings.SetStoreOpen(!settings.IsStoreOpen);
        await ctx.SaveChangesAsync();

recorder.Called("BusinessSettings:InvalidateShipping")
            .Should().BeFalse("nothing delivery-scoped derives from the open/closed flag");
        recorder.Called(nameof(ICatalogCacheService.InvalidateCatalogStructure))
            .Should().BeFalse("no product page field changed");
    }

    [SkippableFact]
    public async Task A_currency_change_orphans_every_cached_product_page()
    {
        await using var ctx = Db.CreateCacheObservedDbContext(_memoryCache, out var recorder);

        // This was a live bug: settings handlers evicted the settings object but not the product
        // pages, so every cached page kept rendering the old currency until its entry expired.
        var settings = await LoadSettingsAsync(ctx);
        settings.UpdateLocale(
            settings.CountryCode,
            settings.CurrencyCode == "INR" ? "USD" : "INR",
            settings.TimeZoneId,
            settings.Culture);
        await ctx.SaveChangesAsync();

        recorder.Called(nameof(ICatalogCacheService.InvalidateCatalogStructure))
            .Should().BeTrue("StorefrontProductResponse renders the currency code from BusinessSettings");
    }

    // -----------------------------------------------------------------------
    // Entities that must not disturb shared caches
    // -----------------------------------------------------------------------

    [SkippableFact]
    public async Task Saving_a_cart_invalidates_nothing_shared()
    {
        await using var ctx = Db.CreateCacheObservedDbContext(_memoryCache, out var recorder);

        var cart = Cart.CreateForCustomer(Guid.NewGuid());
        cart.AddItem(Guid.NewGuid(), null, 2);
        ctx.Carts.Add(cart);
        await ctx.SaveChangesAsync();

        // Carts are per-customer and recomputed per request; evicting shared catalog projections
        // on every basket change would be pure overhead.
        recorder.Calls.Should().BeEmpty("no cached projection embeds cart data");
    }

/// <summary>
    /// The cached settings entry is the whole entity, credentials included. EF marks the owned
    /// Auth entry Modified and leaves the BusinessSettings principal Unchanged, so this would be
    /// missed by a state-based check — serving stale OAuth credentials after a rotation.
    /// </summary>
    [SkippableFact]
    public async Task An_auth_change_still_evicts_the_cached_settings_object()
    {
        await using var ctx = Db.CreateCacheObservedDbContext(_memoryCache, out var recorder);

        var settings = await LoadSettingsAsync(ctx);
        settings.UpdateGoogleCredentials(
            $"diag-client-{Guid.NewGuid():N}", "diag-secret", "https://example.test/callback");
        await ctx.SaveChangesAsync();

        recorder.Called("BusinessSettings:Invalidate")
            .Should().BeTrue("the cached BusinessSettings entry embeds the encrypted OAuth credentials");
    }

    [SkippableFact]
    public async Task Saving_an_outbox_event_invalidates_nothing_shared()
    {
        await using var ctx = Db.CreateCacheObservedDbContext(_memoryCache, out var recorder);

        ctx.OutboxEvents.Add(OutboxEvent.Create("test.event", "{}"));
        await ctx.SaveChangesAsync();

        recorder.Calls.Should().BeEmpty("the outbox is internal plumbing read straight from its table");
    }

    [SkippableFact]
    public async Task A_save_that_changes_nothing_invalidates_nothing()
    {
        await using var ctx = Db.CreateCacheObservedDbContext(_memoryCache, out var recorder);

        await ctx.SaveChangesAsync();

        recorder.Calls.Should().BeEmpty("an empty save cannot have changed a cached projection");
    }

    // -----------------------------------------------------------------------
    // Helper
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns a tracked settings row, creating the singleton if the database has none.
    /// </summary>
    private static async Task<BusinessSettings> LoadSettingsAsync(AppDbContext ctx)
    {
        ctx.ChangeTracker.Clear();

        var settings = await ctx.BusinessSettings.FirstOrDefaultAsync();
        if (settings is not null)
            return settings;

        ctx.BusinessSettings.Add(BusinessSettings.CreateDefault("Cache Test Store"));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        return await ctx.BusinessSettings.FirstAsync();
    }
}