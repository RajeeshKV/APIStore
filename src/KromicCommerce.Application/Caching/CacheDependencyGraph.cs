using KromicCommerce.Domain.Catalog;
using KromicCommerce.Domain.Cart;
using KromicCommerce.Domain.Identity;
using KromicCommerce.Domain.Orders;
using KromicCommerce.Domain.Outbox;
using KromicCommerce.Domain.Promotions;
using KromicCommerce.Domain.Sms;
using KromicCommerce.Domain.Store;
using KromicCommerce.Domain.Webhooks;

namespace KromicCommerce.Application.Caching;

/// <summary>
/// The authoritative map from persisted entity to the cache projections that embed data derived
/// from it.
///
/// <para>
/// This is the single source of truth for cache invalidation across the whole application.
/// Correctness previously rested on every mutation handler remembering to call the right
/// invalidation method; the graph plus the EF save interceptor derives it from what actually
/// changed instead, so a handler that forgets cannot serve stale data.
/// </para>
///
/// <para>
/// Each mapping must be justified by a projection that really embeds the entity's data. Adding a
/// field to a cached DTO means adding the entity here, and
/// <c>CacheDependencyGraphTests</c> fails if a cached projection has no entity mapped to it.
/// </para>
/// </summary>
public static class CacheDependencyGraph
{
    /// <summary>Entity type to the projections it dirties.</summary>
    private static readonly Dictionary<Type, CacheProjection> _entityProjections = new()
    {
        // ---- Product and everything a product page renders -------------------------
        // The product page embeds the product's own fields plus stock, images, attributes,
        // variants, and the denormalised rating summary (RatingAverage/RatingCount). The
        // featured list embeds the same rollup availability. The storefront brand and category
        // lists embed an active ProductCount computed from the products table, so a product write
        // moves those counts even though the product row itself is not in them.
        [typeof(Product)] =
            CacheProjection.StorefrontProduct |
            CacheProjection.StorefrontFeatured |
            CacheProjection.StorefrontCategories |
            CacheProjection.StorefrontBrands,

        // Images appear only on the product page; the featured list shows a primary image URL,
        // which changes only through the Product/ProductImage rows covered above.
        [typeof(ProductImage)] = CacheProjection.StorefrontProduct,

        // A variant changes the availability rollup on both the product page and the featured list.
        [typeof(ProductVariant)] =
            CacheProjection.StorefrontProduct | CacheProjection.StorefrontFeatured,

        // Attributes and their values render only on the product page.
        [typeof(ProductAttribute)] = CacheProjection.StorefrontProduct,
        [typeof(ProductAttributeValue)] = CacheProjection.StorefrontProduct,

        // Stock feeds StockAvailability on the product page and the featured list.
        [typeof(InventoryItem)] =
            CacheProjection.StorefrontProduct | CacheProjection.StorefrontFeatured,

        // ---- Category and brand -----------------------------------------------------
        // Admin and storefront lists obviously. CatalogStructure additionally orphans every
        // cached product page, because a page embeds CategoryName/CategorySlug and
        // BrandName/BrandSlug — renaming or deactivating either makes every page wrong.
        [typeof(Category)] =
            CacheProjection.AdminCategories |
            CacheProjection.StorefrontCategories |
            CacheProjection.AllStorefrontProducts,

        [typeof(Brand)] =
            CacheProjection.AdminBrands |
            CacheProjection.StorefrontBrands |
            CacheProjection.AllStorefrontProducts,

        // ---- Reviews ---------------------------------------------------------------
        // Per-product review pages only.
        //
        // The rating summary a product page renders is NOT derived from this mapping: it is
        // denormalised onto the Product row as RatingAverage/RatingCount, so the product page is
        // already invalidated by the Product mapping above. Every handler that can change the
        // published review set (submit, edit, delete, moderate, admin delete) recalculates the
        // aggregate and marks Product modified in the same save, so product pages are covered
        // without duplicating the dependency here.
        [typeof(ProductReview)] = CacheProjection.ProductReviews,
        [typeof(ReviewHelpfulVote)] = CacheProjection.ProductReviews,

        // ---- Standalone projections -------------------------------------------------
        [typeof(CarouselSlide)] = CacheProjection.StorefrontCarousel,
        [typeof(StorePolicy)] = CacheProjection.PublicPolicies,

        // The settings row itself. Delivery and CurrencyCode refine this at the call site, because
        // only those two have downstream projections — see CacheInvalidationPlanFactory.
        [typeof(BusinessSettings)] = CacheProjection.BusinessSettings,
    };

    /// <summary>
    /// Entities deliberately absent from <see cref="_entityProjections"/>, with the reason.
    ///
    /// This list exists so coverage is auditable rather than accidental: a new DbSet must either
    /// appear in the map above or be listed here, and the graph tests fail if neither happened.
    /// </summary>
    private static readonly Dictionary<Type, string> _intentionallyUncached = new()
    {
        // Identity and session state. Read through the request's own token/user, never cached.
        [typeof(User)] = "Per-principal identity data; read per request from the authenticated token.",
        [typeof(RefreshToken)] = "Per-principal session state; validity is checked against the database on every use.",
        [typeof(ExternalLogin)] = "Only read during the OAuth callback, which is not cached.",
        [typeof(CustomerProfile)] = "Per-customer data; returned directly to the owning customer.",
        [typeof(CustomerAddress)] = "Per-customer data; ownership-checked and returned per request.",
        [typeof(OtpRequest)] = "Short-lived verification state, deliberately never cached.",
        [typeof(OtpSendClaim)] = "Concurrency claim rows, written through raw SQL and never read for display.",

        // Carts and checkout. Recomputed per request and always scoped to one customer or session.
        [typeof(Cart)] = "Per-customer or per-session; subtotals are recomputed on every read.",
        [typeof(CartItem)] = "Part of the per-customer cart projection, which is not cached.",

        // Orders and payments. Customer-scoped and admin-scoped reads are never cached, so an order
        // write has no cached projection to dirty.
        [typeof(Order)] = "Customer-scoped and admin-scoped reads go straight to the database.",
        [typeof(OrderItem)] = "Belongs to the uncached order projection.",
        [typeof(Payment)] = "Read directly for webhook idempotency and reconciliation; caching it would be actively harmful.",
        [typeof(WebhookEvent)] = "Internal idempotency ledger, never read for display.",

        // Wishlist and reviews-adjacent per-customer data.
        [typeof(WishlistItem)] = "Per-customer data, returned only to the owning customer.",

        // Promotions. Validated against the database at checkout and applied to a recomputed
        // cart, so no cached projection embeds a promotion.
        [typeof(Promotion)] = "Checkout re-validates the rule against the database on every order.",
        [typeof(PromotionUsage)] = "Per-customer usage counters, read inside the uncached checkout flow.",

        // SMS configuration. Read through ISmsProviderFactory on demand; the admin endpoints
        // build their status response per request. Nothing is cached, so nothing is invalidated.
        [typeof(SmsProviderConfig)] = "Read per send by ISmsProviderFactory; not cached.",

        // Internal plumbing.
        [typeof(OutboxEvent)] = "Internal outbox row; only the processor reads it, straight from the table.",
    };

    public static IReadOnlyDictionary<Type, CacheProjection> EntityProjections => _entityProjections;

    public static IReadOnlyDictionary<Type, string> IntentionallyUncached => _intentionallyUncached;

    /// <summary>Projections dirtied by a write to <paramref name="entityType"/>.</summary>
    public static CacheProjection For(Type entityType) =>
        _entityProjections.TryGetValue(entityType, out var projection) ? projection : CacheProjection.None;

    /// <summary>True when the entity feeds at least one cached projection.</summary>
    public static bool FeedsCache(Type entityType) =>
        _entityProjections.ContainsKey(entityType);

    /// <summary>True when the entity is a known, deliberately uncached entity.</summary>
    public static bool IsIntentionallyUncached(Type entityType) =>
        _intentionallyUncached.ContainsKey(entityType);

    /// <summary>
    /// True when every entity in the model is accounted for in exactly one of the two maps.
    /// Used by the coverage test so adding a DbSet without a decision cannot pass silently.
    /// </summary>
    public static bool IsFullyMapped(IEnumerable<Type> modelEntityTypes)
    {
        var known = new HashSet<Type>(_entityProjections.Keys.Concat(_intentionallyUncached.Keys));
        return modelEntityTypes.All(known.Contains);
    }
}