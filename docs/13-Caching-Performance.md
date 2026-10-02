# Caching and Performance

## Cache
Use ASP.NET Core `IMemoryCache` in V1. No Redis.

> **Operational constraint — single instance only.**
> `IMemoryCache` is per-process. Invalidating a key on instance **A** does nothing to instance
> **B**. Running more than one API instance silently breaks correctness: an admin edits a product,
> the write hits instance A, A evicts its own copy, and B keeps serving the old page to customers
> until the 60-minute absolute expiry expires. Nothing errors, and no log line appears — the only
> symptom is "the change appears for some customers".
>
> This is safe **only** while the deployment is a single instance. Before scaling out, either move
> to a distributed cache (Redis) or make the storefront read-through unconditional. See *Future*.

## Cache-aside
```text
Request
 -> MemoryCache
 -> miss
 -> PostgreSQL
 -> populate cache
```

## Good cache candidates
- Product lists
- Product details
- Categories
- Navigation
- Homepage
- Business settings
- Static pages
- Promotions
- Delivery configuration
- SEO configuration

## Never use cache as source of truth for
- Payment
- Refund
- Order creation
- Stock validation at checkout
- Customer transactional state
- OTP verification

## Invalidation
After successful admin mutations:
- Update database
- Commit
- Invalidate relevant keys
- Next read repopulates cache

Prefer centralized cache-key definitions.

### Keyed families and epochs

`IMemoryCache` cannot enumerate keys and cannot prefix-delete. Anything whose key varies by a
request parameter therefore cannot be evicted by "matching a prefix", so those families are
addressed through a **monotonically increasing epoch** instead: the epoch is part of the key, and
invalidating means bumping it. Old entries become unreachable immediately and are reclaimed later
by `SizeLimit` / absolute expiry.

| Family | Epoch key | Bumped by | Orphans |
|--------|-----------|-----------|---------|
| Storefront product pages (`…|c{catalog}e{shipping}`) | `store:catalog_epoch` | brand / category mutation | every cached product page |
| Storefront product pages (`…|c{catalog}e{shipping}`) | `store:shipping_epoch` | delivery / COD settings | every cached product page |
| Product review pages (`…|e{epoch}`) | `storefront:reviews_epoch:{productId}` | review write for that product | every cached review page for that product |
| Featured products (`storefront:products:featured:{limit}`) | — (explicit loop) | product, stock, brand, category | limits 1–50 |
| Store policies | — | policy upsert / delete | the single entry |

> **Do not raise `MaxFeaturedLimit` in one place only.** `CatalogCacheKeys.MaxFeaturedLimit` is
> the single source for both the query handler's clamp and the invalidation loop. Two independent
> constants would let the clamp rise while invalidation stayed behind, and the newly permitted
> limits would then never be evicted.

### Dependency matrix

The invalidation must follow the **embedded data**, not the table that was written. A projection
containing a brand name is stale when either the product or the brand changes.

| Mutation | Must evict |
|----------|------------|
| Product create / update / status / images / attributes / variants | product page, featured, admin product, storefront brands, storefront categories (product counts) |
| Stock / inventory / reservation change | product page, featured |
| Brand create / update / delete / image | admin brands, storefront brands, **all product pages** (embed `BrandName`), featured (embeds `BrandName`) |
| Category create / update / delete / image | admin categories, storefront categories, **all product pages** (embed `CategoryName`), featured (embeds `CategoryName`) |
| Review create / moderate / delete / image | product page + featured (rating aggregate), review pages for that product |
| Review helpful vote | review pages for that product **only** — a vote does not change the rating aggregate |
| Delivery / COD settings | business settings, shipping epoch (all product pages embed `DeliveryEstimateDto`) |
| Other business settings | business settings |
| Carousel slide mutation | storefront carousel |
| Store policy upsert / delete | public policies |

Use the graph methods on `ICatalogCacheService` (`InvalidateProductGraph`, `InvalidateStockGraph`,
`InvalidateBrandGraph`, `InvalidateCategoryGraph`, `InvalidateCatalogStructure`) rather than
individual keys, so a call site cannot forget a dependent projection.

Brand and category mutations evict **every** product page rather than only the affected products,
because the affected slugs cannot be known from the cache alone and enumerating is impossible. That
is a deliberate correctness-over-hit-rate trade.

### Known gaps and hazards

- **Per-process invalidation** — see the constraint at the top. The dominant risk in this module.
- **Read-through race.** Invalidation runs *after* `SaveChangesAsync`. A concurrent reader that
  queried the database before the commit and writes to the cache *after* the invalidation will
  re-populate a stale entry that then survives for the full TTL. The window is small but real under
  concurrent admin + storefront load; it is not currently guarded.
- **Dead keys.** `catalog:products:all` and `catalog:product:{id}` are evicted but never written.
  They imply an admin product cache that does not exist; do not read that as coverage.
- **Cache-aside stampede.** A popular product slug expiring triggers one database query per
  concurrent request. No locking or jitter is applied.

## Expiration
Use reasonable absolute/sliding expiration as a safety net, but correctness comes from invalidation.
The default is 60 minutes, so an invalidation gap is not self-healing for up to an hour.

## Performance
- Database indexes
- Pagination
- Projection for read queries
- Avoid N+1
- Async I/O
- Response compression where appropriate
- Browser cache headers
- Cloudinary image transformations
- WebP/AVIF where appropriate
- Lazy-load images
- Avoid unnecessary API calls

## Future
Redis may be introduced only if multiple API instances/shared distributed cache becomes necessary.
Until then, treat "exactly one API instance" as a correctness requirement, not a scaling choice.
