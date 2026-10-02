using FluentAssertions;
using KromicCommerce.Application.Caching;
using KromicCommerce.Domain.Catalog;
using KromicCommerce.Domain.Cart;
using KromicCommerce.Domain.Identity;
using KromicCommerce.Domain.Orders;
using KromicCommerce.Domain.Promotions;
using KromicCommerce.Domain.Sms;
using KromicCommerce.Domain.Store;
using KromicCommerce.Domain.Webhooks;
using Xunit;

namespace KromicCommerce.UnitTests.Application.Caching;

/// <summary>
/// The dependency graph is what makes cache coherence global instead of per-handler, so it is
/// treated as a contract that must be maintained, not a lookup table.
/// </summary>
public sealed class CacheDependencyGraphTests
{
    /// <summary>
    /// Every persisted entity must be accounted for. A new DbSet that nobody classified would
    /// otherwise be a silent cache-coherence hole, which is exactly how the webhook and settings
    /// bugs happened in the first place.
    /// </summary>
    [Fact]
    public void Every_persisted_entity_is_either_cached_or_explicitly_uncached()
    {
        var entities = new[]
        {
            // Catalog
            typeof(Product), typeof(ProductImage), typeof(ProductVariant),
            typeof(ProductAttribute), typeof(ProductAttributeValue), typeof(InventoryItem),
            typeof(Category), typeof(Brand), typeof(ProductReview), typeof(ReviewHelpfulVote),
            typeof(CarouselSlide),
            // Store
            typeof(BusinessSettings), typeof(StorePolicy),
            // Identity
            typeof(User), typeof(RefreshToken), typeof(ExternalLogin), typeof(CustomerProfile),
            typeof(CustomerAddress), typeof(OtpRequest), typeof(WishlistItem),
            // Sms
            typeof(SmsProviderConfig), typeof(OtpSendClaim),
            // Cart
            typeof(Cart), typeof(CartItem),
            // Orders
            typeof(Order), typeof(OrderItem), typeof(Payment),
            // Webhooks
            typeof(WebhookEvent),
            // Promotions
            typeof(Promotion), typeof(PromotionUsage),
        };

        var unclassified = entities
            .Where(t => !CacheDependencyGraph.FeedsCache(t)
                        && !CacheDependencyGraph.IsIntentionallyUncached(t))
            .Select(t => t.Name)
            .ToArray();

        unclassified.Should().BeEmpty(
            "every persisted entity must either feed a cache projection or be listed with a reason " +
            "for being uncached; an unclassified entity is an invisible staleness bug");
    }

    /// <summary>
    /// Guards against an entity being listed in both maps, which would make the two sources of
    /// truth disagree about whether it is cached.
    /// </summary>
    [Fact]
    public void No_entity_is_both_cached_and_intentionally_uncached()
    {
        var cached = CacheDependencyGraph.EntityProjections.Keys;
        var uncached = CacheDependencyGraph.IntentionallyUncached.Keys;

        cached.Intersect(uncached).Select(t => t.Name).Should().BeEmpty();
    }

    [Fact]
    public void Every_intentionally_uncached_entity_carries_a_reason()
    {
        CacheDependencyGraph.IntentionallyUncached.Values
            .Should().OnlyContain(reason => !string.IsNullOrWhiteSpace(reason),
                "an entry without a reason will be re-litigated every time someone reads it");
    }

    /// <summary>
    /// A projection nothing can ever dirty is dead weight, and usually a sign that an entity is
    /// missing from the graph.
    /// </summary>
    [Fact]
    public void Every_projection_is_reachable_from_the_graph()
    {
        // ShippingConfiguration is deliberately unreachable through the plain map: it must fire for
        // a Delivery change specifically, not for every BusinessSettings write, so the interceptor
        // adds it while refining a settings entry rather than mapping it to the whole entity.
        var refinementOnly = CacheProjection.ShippingConfiguration;

        var reachable = CacheDependencyGraph.EntityProjections.Values.ToArray();

        var orphans = Enum.GetValues<CacheProjection>()
            .Where(p => p != CacheProjection.None && p != refinementOnly)
            .Where(p => !reachable.Any(mapped => mapped.HasFlag(p)))
            .ToArray();

        orphans.Should().BeEmpty(
            "every projection must be reachable from at least one entity mapping");
    }

    /// <summary>
    /// Pins the refinement-only contract so it cannot quietly become reachable — if it ever does,
    /// every settings write would start orphaning delivery-scoped product pages.
    /// </summary>
    [Fact]
    public void Shipping_is_not_reachable_from_a_plain_entity_mapping()
    {
        CacheDependencyGraph.EntityProjections.Values
            .Should().OnlyContain(p => !p.HasFlag(CacheProjection.ShippingConfiguration),
                "shipping is added by refining a BusinessSettings Delivery change, not by the map");
    }

    // -----------------------------------------------------------------------
    // Specific dependencies that are easy to get wrong
    // -----------------------------------------------------------------------

    [Fact]
    public void A_product_write_dirties_the_counts_embedded_in_the_brand_and_category_lists()
    {
        var projection = CacheDependencyGraph.For(typeof(Product));

        // The trap: evicting only the product's own page leaves every category and brand showing
        // a stale product count until the entry expires.
        projection.Should().HaveFlag(CacheProjection.StorefrontCategories);
        projection.Should().HaveFlag(CacheProjection.StorefrontBrands);
        projection.Should().HaveFlag(CacheProjection.StorefrontProduct);
        projection.Should().HaveFlag(CacheProjection.StorefrontFeatured);
    }

    [Fact]
    public void A_brand_or_category_write_orphans_every_cached_product_page()
    {
        // Product pages embed CategoryName/Slug and BrandName/Slug, and the affected slugs cannot
        // be enumerated from the cache, so this must go through the all-products projection.
        CacheDependencyGraph.For(typeof(Category))
            .Should().HaveFlag(CacheProjection.AllStorefrontProducts);
        CacheDependencyGraph.For(typeof(Brand))
            .Should().HaveFlag(CacheProjection.AllStorefrontProducts);
    }

    [Fact]
    public void A_stock_write_dirties_availability_but_not_any_product_count()
    {
        var projection = CacheDependencyGraph.For(typeof(InventoryItem));

        projection.Should().HaveFlag(CacheProjection.StorefrontProduct);
        projection.Should().HaveFlag(CacheProjection.StorefrontFeatured);
        projection.Should().NotHaveFlag(CacheProjection.StorefrontCategories);
        projection.Should().NotHaveFlag(CacheProjection.StorefrontBrands);
    }

    [Fact]
    public void A_review_write_invalidates_review_pages_only_directly()
    {
        // The rating summary rendered on the product page is denormalised onto the Product row,
        // not read from the review table. So ProductReview maps to the review pages alone, and the
        // product page is invalidated by the Product mapping instead — every handler that can
        // change the published review set recalculates the aggregate and marks Product modified in
        // the same save.
        //
        // Asserting the Product half here too would duplicate that coverage and hide a real gap if
        // a future review path ever recalculated the aggregate without saving the product.
        CacheDependencyGraph.For(typeof(ProductReview)).Should().Be(CacheProjection.ProductReviews);
        CacheDependencyGraph.For(typeof(ReviewHelpfulVote)).Should().Be(CacheProjection.ProductReviews);

        CacheDependencyGraph.For(typeof(Product))
            .Should().HaveFlag(CacheProjection.StorefrontProduct,
                "the product page renders the rating summary carried on the product row");
    }

    [Fact]
    public void A_cart_or_order_write_dirties_nothing_shared()
    {
        foreach (var entityType in new[]
                 {
                     typeof(Cart), typeof(CartItem), typeof(Order),
                     typeof(OrderItem), typeof(Payment)
                 })
        {
            CacheDependencyGraph.For(entityType).Should().Be(CacheProjection.None);
        }
    }

    [Fact]
    public void An_unmapped_type_returns_none_rather_than_throwing()
    {
        CacheDependencyGraph.For(typeof(string)).Should().Be(CacheProjection.None);
        CacheDependencyGraph.FeedsCache(typeof(string)).Should().BeFalse();
    }

    [Fact]
    public void IsFullyMapped_reports_a_gap_when_an_entity_is_unclassified()
    {
        CacheDependencyGraph.IsFullyMapped([typeof(Product)]).Should().BeTrue();
        CacheDependencyGraph.IsFullyMapped([typeof(Product), typeof(ShippingAddress)])
            .Should().BeFalse();
    }
}