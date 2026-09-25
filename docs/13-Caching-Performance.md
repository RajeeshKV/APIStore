# Caching and Performance

## Cache
Use ASP.NET Core IMemoryCache in V1. No Redis.

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

## Expiration
Use reasonable absolute/sliding expiration as a safety net, but correctness comes from invalidation.

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
