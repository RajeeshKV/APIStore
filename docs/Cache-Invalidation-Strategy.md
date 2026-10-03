# Cache invalidation strategy

How the application guarantees that no read is ever served from a cache that predates the data it
came from, and how that guarantee is maintained as the domain grows.

## The problem this solves

Cache correctness used to rest on every mutation handler remembering to call the right invalidation
method. That is a manual invariant across ~90 `SaveChangesAsync` call sites, and it failed in three
places:

| Symptom | Why it happened |
|----------|-----------------|
| Payment webhook released reserved stock, storefront kept selling it | `HandlePaymentWebhookHandler` never called the stock invalidation |
| Changing the store currency left every product page showing the old code | Settings handlers evicted the settings object, but product pages embed `CurrencyCode` |
| Rotating Google OAuth credentials left the cached settings object holding the old secret | — |

None of these are subtle business bugs. They are the predictable result of depending on 90 people
remembering.

## The design

Invalidation is now **derived from what a save changed**, not from what a handler remembered.

```
SaveChangesAsync
      │
      ├─ SavingChanges   ── capture  which entities changed, and how
      │
      └─ SavedChanges    ── build a CacheInvalidationPlan from CacheDependencyGraph,
                            resolve affected slugs, apply via ICacheInvalidator
```

| Piece | Location | Role |
|-------|----------|------|
| `CacheProjection` | `Application/Caching/CacheProjection.cs` | The evictable cache families, as a `[Flags]` enum |
| `CacheDependencyGraph` | `Application/Caching/CacheDependencyGraph.cs` | **Single source of truth**: entity → projections, plus the entities deliberately left uncached |
| `CacheInvalidationPlan` | `Application/Caching/CacheInvalidationPlan.cs` | One change's full invalidation footprint |
| `ICacheInvalidator` | `Application/Abstractions/Catalog/ICacheInvalidator.cs` | The single write path for eviction |
| `CacheInvalidator` | `Infrastructure/Caching/CacheInvalidator.cs` | Translates a plan into cache operations |
| `IBusinessSettingsCacheInvalidator` | `Application/Abstractions/Store/IBusinessSettingsCacheInvalidator.cs` | Cache-only eviction of the settings entries |
| `CacheInvalidationInterceptor` | `Infrastructure/Caching/CacheInvalidationInterceptor.cs` | EF `SaveChangesInterceptor`; the enforcement point |

Because the interceptor is attached to `AppDbContext`, **no handler can persist a change to a cached
entity without evicting what that change dirties** — including handlers written next year.

### The dependency rule

> Anything required while `AppDbContext` is being constructed must not depend on `AppDbContext`.

The interceptor is resolved *inside* the context's own options configuration, so the whole graph
below it has to terminate without the context:

```
AppDbContext
  → CacheInvalidationInterceptor
    → ICacheInvalidator → CacheInvalidator
      → ICatalogCacheService         → IMemoryCache
      → IBusinessSettingsCacheInvalidator → IMemoryCache + ICatalogCacheService

BusinessSettingsService → AppDbContext          (allowed)
IBusinessSettingsService → BusinessSettingsCacheInvalidator → IMemoryCache   (allowed)
ICacheInvalidator → IBusinessSettingsService   (NOT allowed)
```

`CacheInvalidator` originally took `IBusinessSettingsService` in order to reach `Invalidate()`
and `InvalidateShipping()`. Because that service reads and writes the settings row through
`AppDbContext`, the loop closed and the container **blocked** resolving the context — it did not
even throw. Startup therefore reached `Checking for pending EF Core migrations...` and hung, and
the deployment died at "no open ports" without ever naming a DI problem.

Splitting the eviction out into `IBusinessSettingsCacheInvalidator` fixed it. The interface is
deliberately restricted to eviction: no read, no query, no persistence. Adding any of those would
be the first step back towards the cycle. Handlers still call
`IBusinessSettingsService.Invalidate()`, which now delegates to it, so there is one eviction
implementation and one set of cache keys.

`DependencyInjectionGraphTests` resolves `AppDbContext` through the real registration path and
fails if the graph ever becomes cyclic again. Each resolution runs on a bounded wait, because a
cycle here manifests as a block rather than an exception — a plain `GetRequiredService` would hang
the test run instead of reporting the regression.

### Why capture happens before the save

The plan is captured in `SavingChanges` and applied in `SavedChanges`, because neither end works alone:

- **Not in `SavedChanges`**: EF folds Added rows back to `Unchanged` and detaches Deleted ones by
  then, so the information needed to decide what to evict no longer exists.
- **Not in `SavingChanges`**: the write is not durable yet, so a concurrent read could repopulate the
  cache from the pre-commit state.

### Why the plan is captured per context

One interceptor instance is resolved per DI scope, and a scope can own several `DbContext`s. Plans
are therefore keyed by context in a `ConcurrentDictionary`, so two contexts saving concurrently
cannot overwrite each other's plan.

## The dependency graph

| Entity | Projections invalidated | Why |
|--------|-------------------------|-----|
| `Product` | product page, featured, storefront categories, storefront brands | The brand/category lists embed an **active `ProductCount`**, so a product write moves those counts |
| `ProductImage` | product page | Images render on the product page only |
| `ProductVariant` | product page, featured | A variant changes the availability rollup on both |
| `ProductAttribute` / `ProductAttributeValue` | product page | Attributes render on the product page only |
| `InventoryItem` | product page, featured | Both embed `StockAvailability` |
| `Category` | admin categories, storefront categories, **all product pages** | Pages embed `CategoryName`/`CategorySlug` |
| `Brand` | admin brands, storefront brands, **all product pages** | Pages embed `BrandName`/`BrandSlug` |
| `ProductReview` / `ReviewHelpfulVote` | per-product reviews | The rating summary a product page renders is denormalised onto `Product` as `RatingAverage`/`RatingCount`, so product pages are covered by the `Product` mapping rather than duplicated here |
| `CarouselSlide` | carousel | |
| `StorePolicy` | public policies | |
| `BusinessSettings` | settings object (+ refinements below) | The cached entry is the whole entity, credentials included |

Everything else — carts, orders, payments, wishlists, addresses, users, tokens, promotions, SMS
configuration, outbox, webhooks — is listed in `IntentionallyUncached` **with a reason**, because
their reads are per-customer or recomputed per request.

### Refinements

Two `BusinessSettings` changes have downstream projections that must not fire for *every* settings
write:

- **`Delivery` changed** → `ShippingConfiguration`. Storefront product pages embed a delivery
  estimate. Detected on the **owned** `DeliverySettings` change-tracker entry.
- **`CurrencyCode` changed** → all product pages, which render the currency code.

A settings edit that changes neither — opening/closing the store, editing SEO — evicts only the
settings object, so a tax tweak does not orphan every cached product page.

> **Owned types are a genuine trap.** EF marks the *owned* entry `Modified` and leaves the
> `BusinessSettings` principal **`Unchanged`**. A check based on the owner's state therefore misses
> every `Delivery`, `Auth`, `Email`, `Seo`, `Tax` and `Payment` edit — which is exactly how stale
> OAuth credentials would survive a rotation. `BusinessSettingsChanged` inspects the owned entries
> explicitly for this reason.

## What is *not* covered, and why

### Raw SQL bypasses the change tracker

The `INSERT ... ON CONFLICT` helpers on `AppDbContext` are invisible to the interceptor. All of them
write per-customer or internal data that is never cached — **except** `TryAddProductReviewAsync`,
which writes a cached `ProductReview`. `SubmitReviewHandler` therefore invalidates explicitly.

`RawSqlCacheBoundaryTests` inventories these helpers and fails if a new one appears without a
recorded cache consequence, or if a second one starts writing a cached entity.

### The cache is per-process

`IMemoryCache` is in-process. **With more than one API replica, an invalidation on replica A leaves
replica B serving stale entries for up to `Cache:DefaultExpiryMinutes` (60).**

This is the one remaining gap in "strict data consistency", and it cannot be closed inside the
current architecture: it needs a shared cache (Redis) or a database-backed version stamp that every
replica reads. Adding a distributed cache is an infrastructure decision, so it is not done here.

Practical mitigations until then: run a single replica, or set `Cache:DefaultExpiryMinutes` to the
smallest value tolerable for the data being served.

### Manual invalidation still exists and is now redundant

Handlers keep their explicit `ICatalogCacheService` calls. They are idempotent, so they are harmless,
and removing ~90 call sites is a separate, reviewable change. They are no longer load-bearing.

## How to keep this correct

### Adding a field to a cached response

1. If any persisted entity feeds the new field, confirm it is in `CacheDependencyGraph`. If it is
   not, add it — `CacheDependencyGraphTests` will not catch this on its own.
2. If a brand-new cache entry family is introduced, add a `CacheProjection` member.
   `Every_projection_is_reachable_from_the_graph` fails if a projection can never fire.

### Adding an entity

Add it to **one** of the two maps in `CacheDependencyGraph`. The entity no other map is in — the
unclassified entity is a silent staleness hole.

### Changing which projects are cached

Update the graph *before* the read path starts caching, not after. The graph describes what a
cached projection embeds; it is not a list of what happens to be cached today.

### Structural limits worth knowing

- `SavedChanges` (the synchronous overload) **throws by design**. Resolving affected product slugs
  needs a query, and a synchronous save would otherwise persist changes without evicting. Every
  handler uses `SaveChangesAsync`.
- Cache-entry removal for parameterised projections needs the slug, which stock rows do not carry.
  The interceptor resolves it with one indexed lookup per save rather than orphaning every cached
  product page through the catalog epoch.