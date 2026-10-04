# 38 — Customer Wishlist & Product Reviews

Implementation plan and record. Two customer-facing features that share one architectural shape:
a customer-scoped saved-item list, and customer-authored content attached to a product.

Status: **implemented.** See "Implementation record" at the end for what was built, what was
verified, and the two design points that changed during the work.

---

## 1. Scope

### In scope

| Feature | Shape |
|---------|-------|
| **Wishlist** | Customer-owned list of products (optionally a specific variant). Add, remove, clear, list, and a cheap per-product membership check. |
| **Reviews** | Customer-authored rating (1–5) + title + body, optionally attached to a variant, with up to 3 Cloudinary images. One review per customer per product/variant. Verified-purchase flag computed from delivered orders, never client-supplied. Moderation queue for admins. "Helpful" votes so reviews can be sorted. |

### Non-goals (explicit)

- No questions/answers, no review replies, no merchant responses.
- No multi-variant cart-style grouping in the wishlist. One wishlist item = one product + at most one variant.
- No "move all to cart". It is a natural follow-up but it entangles the wishlist with inventory reservation, which is out of scope here.
- No recommendation engine, no review-based search ranking.
- No tenant scoping. The repository is single-tenant per deployment; introducing `TenantId` here would be a cross-cutting change, not a feature change.
- No wishlist sync between anonymous sessions and accounts. Anonymous wishlist requires a merge strategy and a client-held token; it is deliberately deferred.

---

## 2. Architectural decisions

These were the open questions. Each is decided here with the reasoning, so the
implementation does not silently re-litigate them.

### 2.1 Namespaces

Reuse existing namespaces rather than introducing `Engagement/`:

- `KromicCommerce.Domain.Catalog` → `ProductReview`, `ReviewImage`, `ReviewHelpfulVote`
- `KromicCommerce.Domain.Identity` → `WishlistItem`

Rationale: `ProductReview` is product-scoped and participates in product-detail reads,
so it belongs beside `Product`, `ProductImage`, and `ProductVariant`. `WishlistItem` is
a customer-owned record with no product behaviour beyond a scalar FK, exactly like
`CustomerAddress`, so it belongs beside that. A new top-level namespace would fragment
`DbContext` configuration and mirror nothing that exists.

### 2.2 Ownership is never client-supplied

`CustomerId` is always read from `ICurrentUserService.UserId`. No request body, route
parameter, or query-string value may set or override it. `CustomerAddress` already
establishes this pattern; reviews and wishlist follow it without exception.

### 2.3 One review per customer per product/variant

Enforced by a unique index, not by a handler pre-check. The handler pre-check alone
loses to concurrent submits; the index is the real guard, and the
`PostgresException` is translated into a `409` — the same pattern as the
`OtpSendClaim` `INSERT ... ON CONFLICT DO NOTHING` work.

The index spans a **nullable** `ProductVariantId`, which PostgreSQL treats as
distinct-by-default. A plain unique index would therefore permit unlimited
product-level reviews with `ProductVariantId = NULL`. The index must be created with
`nullsDistinct: false`:

```csharp
builder.HasIndex(r => new { r.CustomerId, r.ProductId, r.ProductVariantId })
       .IsUnique()
       .HasDatabaseName("ux_product_reviews_customer_product_variant")
       .HasFilter(null);   // plus raw SQL: NULLS NOT DISTINCT
```

In EF Core this needs a hand-written migration fragment, because the provider
currently has no first-class `nullsDistinct` API on this version. The migration
must be hand-verified to emit `CREATE UNIQUE INDEX ... NULLS NOT DISTINCT`.
Do not accept a generated index that omits the clause — it is silently wrong and
looks correct in review.

### 2.4 `IsVerifiedPurchase` is computed, not accepted

It is derived from the existence of an order for that customer that contains the
product and has reached `OrderStatus.Delivered`. The request DTO has no such field.
This is the same rule as `Product.StockAvailability` being derived from inventory
rather than stored, and it prevents the most obvious review spam vector.

The check is a projection over `OrderItem` joined to `Order`, not a per-item load.

### 2.5 Rating aggregate is denormalised onto `Product`

`Product` gains `RatingAverage` (`decimal(3,2)`) and `RatingCount` (`int`), both
maintained in the same `SaveChangesAsync` that changes any review's published status.

Why not compute on read: the product-detail response is cached
(`IMemoryCache`, same as carousel public reads), so a computed-only aggregate is
either served stale after review activity, or requires cache invalidation on every
review read as well as every review write. A stored aggregate makes the cache
correctness story identical to the rest of the product response.

Invariant, enforced in a domain service
(`ProductReviewRatingRecalculator`) invoked by every handler that can change the
published set — submit, edit, moderate, delete, product cascade-delete:

```
RatingCount = count(reviews where Status == Published)
RatingAverage = RatingCount == 0 ? 0 : round(sum(Rating) / RatingCount, 2)
```

Recalculate, do not increment. Incremental updates drift the moment any write path
is added later.

Enums persist as strings in this schema, so `ReviewStatus` stores its name.

### 2.6 Reviews are published on submission

Submissions are created `Published` and are public immediately — the storefront list, the product
detail aggregate, and the `rating`/`helpful` sorts all include them in the same request that wrote
them. There is no pre-publication gate: a customer who has just written a review sees it.

The admin levers replace the gate, and all three take effect at once, because every one of them
runs the recalculator in the same save:

- `Pending` — withdraws it from the storefront; the aggregate drops.
- `Rejected` — same, plus a required reason shown back to the author.
- `DELETE` — removes it; the aggregate drops.

A `Pending` review stays visible to its author in `GET /api/v1/reviews/mine` and in an admin list,
so a customer who had theirs pulled can see that it exists and edit or delete it rather than
wondering whether it saved.

### 2.7 No soft delete, consistent with the schema

Nothing else in this database uses soft delete, and audit history on a row the
customer asked to be erased is the wrong default for review content. Reviews and
wishlist items hard-delete. `AuditableEntity` is still used so `CreatedAtUtc`
drives ordering and `UpdatedAtUtc` records edits.

### 2.8 Trust boundary for image uploads

Customer-uploaded review images are a Cloudinary write from an authenticated
non-admin. This needs its own endpoint with its own rate-limit policy rather than
reusing an admin upload route. See §7 — this is the one item where the existing
surface needs confirming before coding.

---

## 3. Domain layer

### 3.1 `WishlistItem`

`src/KromicCommerce.Domain/Identity/WishlistItem.cs`

Mirrors `CustomerAddress`: `AuditableEntity`, static `Create` with validation, no
navigation to `User` (scalar `CustomerId` only, as with `CustomerAddress`).

```csharp
public sealed class WishlistItem : AuditableEntity
{
    private WishlistItem() { } // EF constructor

    public static WishlistItem Create(Guid customerId, Guid productId, Guid? productVariantId = null)

    public Guid CustomerId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid? ProductVariantId { get; private set; }
}
```

`Create` throws `ArgumentException` on an empty `customerId` or `productId`. That is
the only validation; variant validity (exists, belongs to the product, is active) is a
repository concern checked in the handler, because it needs the database.

A variant, once set, is immutable. "Switch this wishlist item to a different variant"
is expressed as remove-then-add, which keeps the unique index simple and the history
honest.

Events: `WishlistItemAddedEvent`, `WishlistItemRemovedEvent`
(`src/KromicCommerce.Domain/Identity/Events/`). Raised on `Create` and on removal
respectively, following `CustomerAddressCreatedEvent`.

### 3.2 `ProductReview`

`src/KromicCommerce.Domain/Catalog/ProductReview.cs`

```csharp
public enum ReviewStatus { Pending, Published, Rejected }
```

```csharp
public sealed class ProductReview : AuditableEntity
{
    private ProductReview() { } // EF constructor

    public static ProductReview Create(
        Guid customerId,
        Guid productId,
        Guid? productVariantId,
        int rating,
        string? title,
        string body,
        bool isVerifiedPurchase,
        ReviewStatus status);

    public Guid CustomerId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid? ProductVariantId { get; private set; }
    public int Rating { get; private set; }
    public string? Title { get; private set; }
    public string Body { get; private set; }
    public bool IsVerifiedPurchase { get; private set; }
    public ReviewStatus Status { get; private set; }
    public DateTime? PublishedAtUtc { get; private set; }
    public int HelpfulCount { get; private set; }
    public IReadOnlyCollection<ReviewImage> Images { get; private set; }
}
```

Domain guards:

| Guard | Rule |
|-------|------|
| `Rating` | `1..5` inclusive, else `ArgumentException` |
| `Body` | required, trimmed, max 4000 |
| `Title` | optional, trimmed, max 120 |
| `Images` | max 3, enforced in `AddImage`, not only in the validator |

Behaviour methods:

- `Edit(int rating, string? title, string body)` — content only. Does **not** change
  `Status`, `PublishedAtUtc`, `IsVerifiedPurchase`, or ownership. A published review
  that a customer edits stays published; this is stated explicitly because the
  alternative (reset to `Pending` on every edit) is a real product decision and a
  common source of review disappearance complaints.
- `AddImage(MediaAsset asset, int sortOrder)` — throws past 3.
- `RemoveImage(Guid imageId)` — throws when the image does not belong to this review.
- `SetStatus(ReviewStatus next, string? moderationReason = null)` — the only place
  `Status` or `PublishedAtUtc` changes. Stamps `PublishedAtUtc` on first transition to
  `Published` and clears it on leaving `Published`. Transitions to `Rejected` store the
  reason (max 500). Transitions to the same status are no-ops.
- `SetHelpfulCount(int count)` — never negative.
- `SetVerifiedPurchase(bool)` — called by the handler only, never by a DTO binding.

Domain events (`src/KromicCommerce.Domain/Catalog/Events/`):
`ProductReviewSubmittedEvent`, `ProductReviewEditedEvent`,
`ProductReviewStatusChangedEvent`, `ProductReviewDeletedEvent`.

`ProductReviewDeletedEvent` must carry the `ProductId` so the aggregate recalculator
can be driven off the event rather than off handler bookkeeping.

### 3.3 `ReviewImage`

`src/KromicCommerce.Domain/Catalog/ReviewImage.cs` — a direct copy of the `ProductImage`
shape, since the persistence shape is identical: `Entity`, `ReviewId`, owned
`MediaAsset`, `SortOrder`. `IsPrimary` is omitted because gallery order alone is
sufficient.

`MediaAsset` is a `ValueObject` mapped with `OwnsOne` (as `ProductImage` already does),
so this needs no new column strategy.

### 3.4 `ReviewHelpfulVote`

`src/KromicCommerce.Domain/Catalog/ReviewHelpfulVote.cs` — `Entity`, `ReviewId`,
`CustomerId`, `CreatedAt`. Unique index `(ReviewId, CustomerId)`.

One row per customer per review gives the "helpful" toggle for free: `POST` inserts,
the same `POST` again deletes. No flag column to keep consistent with the counter.

`ProductReview.HelpfulCount` is a denormalised count of these rows, recalculated by the
same domain service as the rating.

Without this entity, `helpful` is not a real sort and `HelpfulCount` has no source.

---

## 4. Persistence

### 4.1 EF configurations

`src/KromicCommerce.Infrastructure/Persistence/Configurations/`

- `WishlistItemConfiguration` — table `wishlist_items`; FK `CustomerId → users.Id`
  with `DeleteBehavior.Cascade`; FK `ProductId → products.Id` cascade; FK
  `ProductVariantId → product_variants.Id` `SetNull`; index
  `(CustomerId, ProductId, ProductVariantId)` unique, **`NULLS NOT DISTINCT`**;
  index `(CustomerId, CreatedAtUtc)` for the list ordering.
- `ProductReviewConfiguration` — table `product_reviews`; FKs as above; `Rating`
  `HasConversion<string>()` on `Status`; `Rating` column named `Rating` with a
  `CHECK (Rating BETWEEN 1 AND 5)`; `Body` max 4000; `Title` max 120;
  `ModerationReason` max 500 nullable; unique index as in §2.3; index
  `(ProductId, Status, PublishedAtUtc)` for the public list; index
  `(CustomerId, CreatedAtUtc)`.
- `ReviewImageConfiguration` — `OwnsOne(a => a.Asset)` with the same inline mapping
  `ProductImageConfiguration` uses.
- `ReviewHelpfulVoteConfiguration` — unique `(ReviewId, CustomerId)`.
- `ProductConfiguration` — add `RatingAverage`, `RatingCount` (defaults `0`),
  and `HasMany(r => r.Reviews)` with cascade delete.

### 4.2 `DbContext`

Add `DbSet<WishlistItem>`, `DbSet<ProductReview>`, `DbSet<ReviewHelpfulVote>` to
`ApplicationDbContext`. `ReviewImage` is reachable through `ProductReview.Images` and
needs no `DbSet`, matching `ProductImage`.

### 4.3 Migration

Hand-authored, named `20261002000000_CustomerWishlistAndProductReviews`. Do not let
`dotnet ef migrations add` generate the unique indexes unreviewed — verify that
`NULLS NOT DISTINCT` is present on `ux_product_reviews_customer_product_variant` and on
the wishlist index, and that `CHECK` constraints survived.

Post-generation review checklist:

1. Both unique indexes carry `NULLS NOT DISTINCT`.
2. `CHECK (Rating BETWEEN 1 AND 5)` present.
3. Cascade paths: product → reviews → review images; user → reviews and wishlist items.
4. `Product.RatingAverage` is `numeric(3,2)` NOT NULL default `0`.

---

## 5. Application layer

`src/KromicCommerce.Application/Features/`

### 5.1 Wishlist — `Features/Customer/Wishlist/`

| File | Contents |
|------|----------|
| `WishlistContracts.cs` | `WishlistItemResponse`, `AddWishlistItemRequest`, `WishlistStatusResponse` |
| `WishlistCommands.cs` | `AddWishlistItemCommand`, `RemoveWishlistItemCommand`, `ClearWishlistCommand` |
| `WishlistQueries.cs` | `GetWishlistQuery`, `GetWishlistStatusQuery` |
| `WishlistCommandHandlers.cs` | |
| `WishlistQueryHandlers.cs` | |

`WishlistItemResponse` returns a product snapshot — `ProductId`, name, slug, price,
`ImageUrl`, `StockAvailability`, `CanPurchase` — projected in the query, never by
loading entities and mapping in memory. It **must not** expose `OnHand` or `Reserved`;
the existing public stock contract is `StockAvailability` + `CanPurchase` only.

`AddWishlistItemHandler` steps: resolve `CustomerId` from `ICurrentUserService` →
verify the product exists and is active → verify the variant, if given, exists, belongs
to that product, and is active → attempt insert → on unique violation return the
existing item (`200`, `isNew: false`) rather than a `409`.

Idempotent add is deliberate. A wishlist double-tap is a UX accident, not an error, and
the storefront's heart button needs no disable-and-retry dance.

`GetWishlistStatusQuery` exists so a product card or grid can render filled/empty
hearts for 20 products in one request instead of 20 per-item calls. It accepts a
product-id list and returns the intersection with the customer's saved items.

### 5.2 Reviews — `Features/Catalog/Reviews/`

| File | Contents |
|------|----------|
| `ReviewContracts.cs` | `ProductReviewSummaryResponse`, `CreateReviewRequest`, `UpdateReviewRequest`, `ReviewListResponse`, `ReviewImageResponse`, `ModerateReviewRequest`, `AdminReviewResponse` |
| `ReviewCommands.cs` | `SubmitReviewCommand`, `EditReviewCommand`, `DeleteReviewCommand`, `ToggleReviewHelpfulCommand`, `ModerateReviewCommand` |
| `ReviewQueries.cs` | `GetProductReviewsQuery`, `GetMyReviewsQuery`, `GetAdminReviewsQuery`, `GetAdminReviewQuery` |
| `ReviewCommandHandlers.cs` | |
| `ReviewQueryHandlers.cs` | |
| `ProductReviewRatingRecalculator.cs` | |

`ReviewListResponse` wraps `PagedResponse<ProductReviewSummaryResponse>` and carries the
aggregate (`ratingAverage`, `ratingCount`, plus a per-star histogram) because the public
consumer needs the summary and the page in one round trip.

Sort options: `recent` (default, `PublishedAtUtc` desc, `Id` tiebreak),
`helpful` (`HelpfulCount` desc, `PublishedAtUtc` desc), `rating` (`Rating` desc,
`PublishedAtUtc` desc). Every sort has a `PublishedAtUtc` then `Id` tail so pagination
is stable — the same tie-break discipline the carousel public ordering already uses.

`ToggleReviewHelpfulHandler` deletes when the vote exists, inserts otherwise, then
recalculates. A reviewer voting on their own review is rejected.

`SubmitReviewHandler` steps: resolve `CustomerId` → verify product active → verify
variant if given → compute `IsVerifiedPurchase` from delivered orders → derive initial
`Status` from the review policy in business settings → insert (unique index is the
concurrency guard; `PostgresException` 23505 → `409 PRODUCT_REVIEW_ALREADY_EXISTS`) →
recalculate aggregate → save once.

Every write path recalculates the aggregate and calls `SaveChangesAsync` exactly once.
This is the discipline the SMS provider work established; it is not optional here.

---

## 6. API surface

All routes are consistent with existing conventions: `/api/v1/...`, MediatR handlers
behind controllers, `ICurrentUserService` for ownership, envelope responses,
`ProblemDetails` for failures.

### Customer

| Method | Route | Auth | Result |
|--------|-------|------|--------|
| `GET` | `/api/v1/wishlist?page=&pageSize=` | customer | `200` `PagedResponse<WishlistItemResponse>` |
| `POST` | `/api/v1/wishlist` | customer | `201` new / `200` existing |
| `DELETE` | `/api/v1/wishlist/{productId}?productVariantId=` | customer | `204` |
| `DELETE` | `/api/v1/wishlist` | customer | `204` |
| `GET` | `/api/v1/wishlist/status?productIds=` | customer | `200` `WishlistStatusResponse` |
| `GET` | `/api/v1/products/{productId}/reviews?page=&pageSize=&sort=&rating=` | anonymous | `200` `ReviewListResponse` |
| `POST` | `/api/v1/products/{productId}/reviews` | customer | `201`, or `200` when auto-published |
| `GET` | `/api/v1/reviews/mine?page=&pageSize=` | customer | `200` `PagedResponse<ProductReviewSummaryResponse>` |
| `PUT` | `/api/v1/reviews/{reviewId}` | customer (owner) | `200` |
| `DELETE` | `/api/v1/reviews/{reviewId}` | customer (owner) | `204` |
| `POST` | `/api/v1/reviews/{reviewId}/helpful` | customer | `200` `{ isHelpful, helpfulCount }` |

### Admin

| Method | Route | Result |
|--------|-------|--------|
| `GET` | `/api/v1/admin/reviews?status=&productId=&rating=&search=&page=&pageSize=` | `200` `PagedResponse<AdminReviewResponse>` |
| `GET` | `/api/v1/admin/reviews/{reviewId}` | `200` `AdminReviewResponse` |
| `PUT` | `/api/v1/admin/reviews/{reviewId}/status` | `200` `AdminReviewResponse` |
| `DELETE` | `/api/v1/admin/reviews/{reviewId}` | `204` |

Review moderation needs no separate integration endpoint; there is no provider or
credential involved.

### Error codes

`WISHLIST_ITEM_NOT_FOUND`, `PRODUCT_REVIEW_NOT_FOUND`,
`PRODUCT_REVIEW_ALREADY_EXISTS`, `PRODUCT_REVIEW_NOT_OWNED`,
`PRODUCT_REVIEW_SELF_VOTE`, `PRODUCT_REVIEW_IMAGE_LIMIT`,
`PRODUCT_NOT_PURCHASABLE`, `PRODUCT_VARIANT_MISMATCH`. These follow the existing
uppercase-snake style used by `PHONE_VERIFICATION_NOT_PENDING`.

---

## 7. Customer image upload

**Resolved: no customer-capable upload endpoint exists. A dedicated one is required.**

Every current `UploadImageAsync` call site is behind `[Authorize(Policy = "AdminOnly")]`:

| Controller | Route | Auth |
|------------|-------|------|
| `ProductImagesController` | `POST /api/v1/products/{productId}/images` | `AdminOnly` |
| `CarouselController`, `BrandsController`, `CategoriesController` | admin upload routes | `AdminOnly` |

Widening any of those to customers would hand the entire catalog and admin media
surface to any authenticated user. Not acceptable. So:

**Add `POST /api/v1/reviews/images`**, `[Authorize]` customer-only, calling
`ICloudinaryService.UploadImageAsync` directly, returning an asset descriptor
(`publicId`, `secureUrl`, `format`, `width`, `height`) that the review-create command
accepts as input for `ReviewImage`.

Two safeguards, both required:

1. **New rate-limit policy.** Existing policies are `general`, `auth`, `otp`,
   `password-reset` — none fit a per-user upload quota. Add a `MediaUploadPolicy` to
   `RateLimitingExtensions`, applied per customer ID rather than per IP, so one abusive
   account cannot exhaust the policy for an entire NAT or office egress.
2. **Origin validation.** The returned `secureUrl` must be checked against
   `https://res.cloudinary.com/` before it is stored, so a tampered client cannot
   persist an arbitrary URL into review content that other customers render.

Note that an orphan-upload problem follows from a two-step upload-then-create flow: a
customer can upload without ever submitting a review, leaving unreferenced Cloudinary
assets. Accept that for now and document it; a cleanup job for orphaned review assets
is a reasonable follow-up, but building it up front would be premature without usage
data.

---

## 8. Caching and outbox

- **Product detail cache** must be invalidated whenever `RatingAverage` or
  `RatingCount` changes — that is, on submit-with-auto-publish, edit, moderate, delete,
  and product deletion. Same invalidation mechanism the carousel uses.
- **Review list cache** keyed by `(productId, sort, page, pageSize, rating)`, invalidated
  on the same events. Public reads are cached; **admin lists are not**, matching the
  carousel split.
- Outbox: review and wishlist domain events flow through the existing outbox so
  integrations can react. No new outbox infrastructure.

---

## 9. Tests

Follows the existing structure: domain unit tests, handler unit tests with fakes, and
integration tests against real PostgreSQL where a database guarantee is what is
actually under test.

### Unit

- `WishlistItem.Create` rejects empty `customerId`/`productId`; trims nothing it
  should not; preserves `productVariantId`.
- `ProductReview.Create` rejects rating `0`, `6`, and negative; rejects empty body;
  rejects title over 120; rejects body over 4000.
- `AddImage` throws on the 4th image; `RemoveImage` throws for a foreign image id.
- `SetStatus` stamps `PublishedAtUtc` on entry to `Published`, clears it on exit,
  is a no-op for the same status, and caps the moderation reason at 500.
- `Edit` does not mutate `Status`, `PublishedAtUtc`, or `IsVerifiedPurchase`.
- `SetHelpfulCount` rejects negatives.

### Handler

- `CustomerId` is taken from `ICurrentUserService` and a conflicting body field, if one
  ever appears, is ignored rather than honoured.
- Non-owner edit/delete/moderate returns `403`/`404` and writes nothing.
- Submit derives `IsVerifiedPurchase` from delivered orders; a body field named
  `isVerifiedPurchase` has no effect. **Negative test — required.**
- Reviewing one's own review for helpfulness is rejected.
- Duplicate submit for the same customer/product/variant returns `409`, not a
  duplicate row.
- Submit yields `Published` + `PublishedAtUtc`, and the review is readable from the storefront
  and counted in the aggregate in the same request.
- Returning a review to `Pending`, rejecting it, or deleting it each withdraw it from the
  storefront and drop it from the aggregate.
- Aggregate recalculation covers publish, unpublish, reject, edit, and delete-to-zero
  (`RatingAverage` returns to `0`, not left at the last value).
- Clear-wishlist deletes only the calling customer's rows.

### Integration (real PostgreSQL)

- `NULLS NOT DISTINCT` actually holds: two product-level reviews with
  `ProductVariantId = NULL` from one customer → second insert violates the index.
  This test is the reason the index clause was specified by hand; it must fail if
  someone regenerates the migration without it.
- Variant-specific and product-level reviews coexist (null ≠ variant id).
- Deleting a product cascades to reviews, review images, and helpful votes.
- Deleting a customer cascades to reviews and wishlist items.
- Concurrent duplicate submits from one customer → exactly one row.
- Aggregate on `Product` matches a recomputed aggregate from the reviews table after a
  mixed sequence of writes.

### Mutation checks

Per the practice already used for the OTP guards, deliberately break each guard and
confirm the corresponding test fails. A guard proven non-vacuous is the deliverable,
not the passing test.

---

## 10. Documentation

| Document | Change |
|----------|--------|
| `docs/API-Reference.md` | All routes, request/response shapes, error codes, auth requirements, paging. |
| `docs/05-Catalog-Inventory.md` | Review and rating-aggregate section. |
| `docs/02-Domain-Model.md` | New aggregates and their invariants. |
| `docs/11-Admin.md` | Review moderation queue. |
| `docs/13-Caching-Performance.md` | Review and product-detail invalidation rules. |
| `docs/19-Requirements-Traceability.md` | Traceability rows. |

---

## 11. Build order

Phases are ordered so each is independently testable and leaves the tree green.

1. **Domain** — entities, enum, events, domain service. Unit tests green.
2. **Persistence** — configurations, `DbContext` sets, hand-verified migration.
   Integration tests green, including `NULLS NOT DISTINCT`.
3. **Wishlist read path** — `GET /wishlist`, `GET /wishlist/status`. No writes yet.
4. **Wishlist write path** — add/remove/clear, idempotent add, cascade behaviour.
5. **Review submit + aggregate** — the core, with the concurrent-submit test.
6. **Review read paths** — public list with sorting and histogram, `mine`, admin list.
7. **Review edit/delete/moderate/helpful** — every path recalculates the aggregate.
8. **Image upload** — new customer-scoped endpoint per §7, with its own rate-limit
   policy and origin validation.
9. **Cache invalidation** — asserted in integration tests, not by inspection.
10. **Docs** — API reference and the rest, once endpoints are live-verified.

Steps 1–7 deliver a complete, useful feature with no images. Images are the last
addition, not a blocker, so the §7 ambiguity does not stall the bulk of the work.

---

## 12. Risks

| Risk | Mitigation |
|------|------------|
| Migration generated without `NULLS NOT DISTINCT` | Hand-author the index; dedicated integration test proves the constraint |
| Aggregate drifts from reality | Recalculate (never increment) in every write path; integration test compares stored vs recomputed |
| Review spam via unverified purchases | `IsVerifiedPurchase` computed server-side, single review per customer enforced by index |
| Pagination duplicates/skips on tied sorts | `PublishedAtUtc` then `Id` tail on every sort |
| Customer upload surface widened by accident | Resolve §7 explicitly; admin endpoints stay admin-only |
| Scope creep into recommendations or anonymous wishlist sync | §1 non-goals are explicit; defer both |
---

## Implementation record

### Built

**Domain** � `WishlistItem`, `ProductReview`, `ReviewImage`, `ReviewHelpfulVote`, `ReviewStatus`,
`ReviewRatingAggregate`, four catalog events and two identity events. `Product` gained
`RatingAverage` / `RatingCount` and a `SetRatingAggregate` method.

**Persistence** � four EF configurations, two new `DbContext` sets (plus one reachable through
the `ProductReview.Images` navigation), and migration `20261002074726_CustomerWishlistAndProductReviews`.

**Application** � wishlist (5 handlers) and reviews (9 handlers plus a shared
`ProductReviewRatingRecalculator`), FluentValidation validators, and a per-product review cache
epoch. **API** � `WishlistController`, `ProductReviewsController` (public list + customer
submits), `ReviewsController` (own-review operations and image upload), `AdminReviewsController`.

### Two design points that changed during the work

**Republish restamps `PublishedAtUtc` rather than preserving the original.** �3.2 originally
specified `??=` so a re-approved review would keep its first publish time. That is unreachable:
leaving `Published` clears `PublishedAtUtc`, so there is never an old value to preserve. A unit
test caught the contradiction. It now stamps on every entry into `Published`, which also means
"sort by recent" reflects when a review became visible again rather than when it was first
written.

**The duplicate guard is an atomic insert, not a catch-and-detach.** �5.2 described catching
`DbUpdateException` on the unique index and re-reading the winner. That needs `DbContext.Entry`,
which `IApplicationDbContext` does not expose. It was replaced with
`TryAddProductReviewAsync` / `TryAddWishlistItemAsync` on the context, using
`INSERT � ON CONFLICT DO NOTHING` � the same approach already used by the cart upsert and the OTP
send claim, and race-free without exception handling. Note this differs from the handler-based
approach in �12's risk table, which is why the context methods exist.

### Verified

| Check | Result |
|-------|--------|
| Migration applies to real PostgreSQL | Clean, all migrations |
| `NULLS NOT DISTINCT` present on both unique indexes | Confirmed in `pg_indexes` |
| Duplicate NULL-variant review rejected | Unique violation raised |
| Duplicate wishlist entry rejected | Unique violation raised |
| Rating 6 rejected | `ck_product_reviews_rating` violation |
| 8-way concurrent submits, one customer | Exactly one row; exactly one winner |
| 8-way concurrent wishlist adds | Exactly one row; exactly one winner |
| Product delete cascade | Reviews, images, and votes removed |
| Variant delete | `SetNull` � review survives |
| Aggregate after moderation sequence | Matches the published set |
| Aggregate after last review removed | Returns to `0 / 0` |
| Helpful count after last vote withdrawn | Returns to `0` |
| Public listing excludes `Pending` | Confirmed |
| Tie-break ordering stability | Confirmed across repeated reads |
| Unit tests | 1026 passing (942 before this work + 84 new) |
| Review integration tests | 15 passing |
| Trust-boundary unit tests | 23 passing |
| Mutation checks | `isVerifiedPurchase` DTO field and URL origin validation both fail when inverted |

Full detail: `docs/API-Reference.md`, sections "Customer Wishlist" and "Product Reviews".

### Known pre-existing failures (not caused by this work)

`EnumStringPersistenceTests` (2), `OrderItemInventoryLifecycleTests` (1) and
`PaymentRefundSchemaTests` (6) fail on the current tree. Verified pre-existing by running the
integration suite with `ReviewIntegrationTests` excluded: the identical 9 failures occur. They
are unrelated to this feature and were left untouched.
