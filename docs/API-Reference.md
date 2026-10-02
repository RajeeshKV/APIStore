# Kromic Commerce â€” Backend API Reference

All endpoints follow the pattern `/api/v1/...` and return JSON.

**Authentication:** JWT Bearer token in the `Authorization: Bearer <token>` header.  
**Pagination:** Paginated endpoints return `PagedResponse<T>` with `items`, `page`, `pageSize`, `totalCount`, `totalPages`, `hasNextPage`, `hasPreviousPage`.  
**Error envelope:**
```json
{ "success": false, "error": { "code": "ERROR_CODE", "message": "Human-readable message" } }
```

---

## Auth â€” `POST /api/v1/auth/...`

The system has two completely separate authentication domains. Customer and admin authentication are independent; they share token infrastructure but nothing else.

### Customer authentication â€” Google Sign-In only

Customers authenticate exclusively via Google Sign-In. There is no customer password registration, no customer email/password login, and no customer password reset.

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| POST | `/api/v1/auth/google` | None | Customer Google Sign-In |
| POST | `/api/v1/auth/refresh` | None | Rotate refresh token, get new access token |
| POST | `/api/v1/auth/logout` | Bearer | Revoke current device refresh token |
| POST | `/api/v1/auth/logout-all` | Bearer | Revoke all refresh tokens, increment TokenVersion |

**Google Sign-In flow:**

```
Customer clicks "Continue with Google"
       â†“
Google Sign-In completes in the browser/app
       â†“
Client receives a Google ID token (credential)
       â†“
POST /api/v1/auth/google  { idToken, deviceHint? }
       â†“
Backend validates ID token against Google's public keys
       â†“
Backend finds or creates customer account (keyed by Google subject, not email)
       â†“
Backend issues application JWT + refresh token
       â†“
200 { accessToken, refreshToken, accessTokenExpiresInSeconds, tokenType }
```

**Google request:** `{ idToken, deviceHint? }`  
**The Google ID token is validated server-side â€” it is never used as the application authorization credential.**

**Account resolution on Google callback:**

| Scenario | Outcome |
|----------|---------|
| Google subject already linked | Returns existing customer |
| Google subject not found, email matches existing account | Links Google identity to existing account |
| Google subject not found, no email match | Creates new customer + CustomerProfile |
| Account deactivated | 403 `AUTH_ACCOUNT_INACTIVE` |
| Invalid / expired Google token | 401 `AUTH_GOOGLE_INVALID` |

**Customer identity key:** Google `sub` (subject) claim â€” stable across email changes. Email is stored as reference only and is never used as the primary identity key.

---

### Admin authentication â€” username or email + password

Admins authenticate with a username or email address plus a password. Google Sign-In is not used for admin authentication.

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| POST | `/api/v1/auth/login` | None | Admin login (username or email + password) |
| POST | `/api/v1/auth/refresh` | None | Rotate refresh token, get new access token |
| POST | `/api/v1/auth/logout` | Bearer | Revoke current device refresh token |
| POST | `/api/v1/auth/logout-all` | Bearer | Revoke all refresh tokens, increment TokenVersion |
| POST | `/api/v1/auth/request-password-reset` | None | Send reset token to admin email (always 204) |
| POST | `/api/v1/auth/reset-password` | None | Complete reset using email + token |

**Login request:** `{ identifier, password, deviceHint? }` â€” `identifier` accepts either an email address or a username.

**Admin login flow:**

```
POST /auth/login  { identifier: "admin@example.com" | "kromic_admin", password }
       â†“
Lookup by NormalizedEmail OR NormalizedUsername (case-insensitive)
       â†“
Verify password (PBKDF2/HMAC-SHA256)
       â†“
200 { accessToken, refreshToken, accessTokenExpiresInSeconds, tokenType }
```

**Admin password reset flow:**

```
POST /auth/request-password-reset  { email }
       â†’ Always 204 (never reveals whether the email exists)
       â†’ If admin found: generates cryptographically random token,
         stores SHA-256 hash, sends raw token via Brevo email
       â†’ Token TTL: 15 minutes, single-use

POST /auth/reset-password  { email, token, newPassword, confirmPassword }
       â†’ Validates token hash, checks expiry
       â†’ On success: updates password hash, increments TokenVersion,
         revokes all refresh tokens (all devices logged out)
       â†’ Token is cleared after first use
```

**Password reset request:** `{ email }` â€” never reveals whether the account exists (enumeration-safe).  
**Reset password request:** `{ email, token, newPassword, confirmPassword }`  
Rate-limited: 3 requests / 15 minutes / IP for both reset endpoints.

---

### Shared token response

All authentication endpoints return the same token envelope:

```json
{
  "accessToken": "eyJ...",
  "refreshToken": "raw-refresh-token",
  "accessTokenExpiresInSeconds": 900,
  "tokenType": "Bearer"
}
```

**Token properties:**

| Property | Value |
|----------|-------|
| Algorithm | HMAC-SHA256 |
| Access token lifetime | 15 minutes (configurable) |
| Refresh token lifetime | 30 days (configurable) |
| Refresh token storage | SHA-256 hash only â€” raw token returned once, never stored |
| Rotation | Every refresh issues a new token and revokes the old one |
| Reuse detection | Reusing a revoked token revokes the entire token family |
| Token versioning | `tv` claim embedded in JWT; incremented on logout-all and password reset |

**JWT claims:** `sub` (userId), `email`, `role` (Customer/Admin), `tv` (tokenVersion), `jti` (unique token ID).

**Refresh token rotation:**

```
POST /auth/refresh  { refreshToken, deviceHint? }
       â†“
Hash incoming token â†’ lookup by hash
       â†“
If already revoked â†’ revoke all tokens for this user â†’ 401 (reuse detected)
If expired â†’ 401
       â†“
Issue new refresh token, revoke old one (linked for audit chain)
       â†“
200 { accessToken, refreshToken, ... }
```

**Logout:**

```
POST /auth/logout  { refreshToken }         â€” revokes single device token
POST /auth/logout-all                       â€” revokes all tokens + increments TokenVersion
```  

---

## Admin Bootstrap â€” `POST /api/v1/admin/bootstrap`

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| POST | `/admin/bootstrap` | None (secret required) | Create first admin (one-time only) |

**Request:** `{ email, password, firstName, lastName, businessName, bootstrapSecret, username? }`  
Rejected with 409 if any admin already exists. Rate-limited: 3 requests per 15 minutes per IP.

---

## Store Settings â€” Admin

All require `AdminOnly`.

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/v1/admin/settings` | Full business settings |
| PUT | `/api/v1/admin/settings/basic` | Name, contact, social links |
| PUT | `/api/v1/admin/settings/locale` | Country, currency, timezone, culture |
| PUT | `/api/v1/admin/settings/delivery` | Shipping fees, COD, delivery days |
| PUT | `/api/v1/admin/settings/auth` | Auth methods, OTP config. `smsProvider` optional â€” provider is selected via `/admin/integrations/sms` |
| PUT | `/api/v1/admin/settings/email` | Email mode and sender identity |
| PUT | `/api/v1/admin/settings/seo` | Meta title, description, favicon |
| PUT | `/api/v1/admin/settings/status` | Open/closed state |
| GET | `/api/v1/admin/tax` | Current tax configuration |
| PUT | `/api/v1/admin/tax` | Update tax config (enabled, %, inclusive, label) |

---

## Store Settings â€” Public

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/api/v1/store/settings` | None | Public store info (no secrets) |

Returns: store name, locale, hours, delivery config, SEO meta, social links (WhatsApp, LinkedIn, Instagram, Facebook, Twitter, YouTube), and safe tracking IDs for client-side analytics â€” never email keys, JWT secrets, or provider credentials.

**Response includes `tracking` object:**
```json
{
  "tracking": {
    "googleAnalyticsMeasurementId": "G-XXXXXXXXXX",  // null if not configured
    "metaPixelId": "1234567890"                       // null if not configured
  }
}
```
Frontend reads these and injects analytics scripts client-side. Null = integration not enabled for this store.

---

## Policies

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/api/v1/store/policies` | None | All published policies |
| GET | `/api/v1/admin/policies` | Admin | All policies including drafts |
| PUT | `/api/v1/admin/policies` | Admin | Upsert policy (create or update by type) |
| DELETE | `/api/v1/admin/policies/{id}` | Admin | Delete policy |

Policy types: `TermsConditions`, `PrivacyPolicy`, `RefundPolicy`, `CancellationPolicy`, `ReturnPolicy`, `ShippingPolicy`, `OrderPolicy`.

---

## Carousel â€” Home page

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/api/v1/store/carousel` | None | Public carousel: active slides only, ordered |
| GET | `/api/v1/admin/carousel` | Admin | All slides including inactive. `?activeOnly=true` filters |
| GET | `/api/v1/admin/carousel/{id}` | Admin | One slide (admin view) |
| POST | `/api/v1/admin/carousel` | Admin | Create slide |
| PUT | `/api/v1/admin/carousel/{id}` | Admin | Update slide |
| DELETE | `/api/v1/admin/carousel/{id}` | Admin | Delete slide |
| PUT | `/api/v1/admin/carousel/{id}/image` | Admin | Upload or replace the slide image |
| DELETE | `/api/v1/admin/carousel/{id}/image` | Admin | Remove the slide image |

The image is uploaded separately, after creation, exactly as for categories and brands. A slide
with no image is a half-finished draft and is **not** returned by the public endpoint.

Accepted image types: JPEG, PNG, WebP, GIF, AVIF. Maximum 10 MB. Uploads go to Cloudinary under
the `carousel/{id}` folder; the returned `publicId` and `secureUrl` are stored on the slide.

### Requests

`POST /api/v1/admin/carousel`

| Field | Type | Required | Constraints |
|-------|------|----------|-------------|
| `title` | string | Yes | 1â€“200 characters |
| `subtitle` | string \| null | No | â‰¤ 500 characters |
| `ctaText` | string \| null | No | â‰¤ 50 characters. A label such as "Shop Now", not a link |
| `sortOrder` | int | No (default `0`) | â‰¥ 0. Lower sorts first |
| `isActive` | bool | No (default `false`) | Whether the storefront shows the slide |

`PUT /api/v1/admin/carousel/{id}` takes the same fields, all required on update.

### Public response

`GET /api/v1/store/carousel` returns an array (empty when nothing is configured â€” never a 404):

| Field | Type | Description |
|-------|------|-------------|
| `id` | guid | Slide id |
| `title` | string | Headline |
| `subtitle` | string \| null | Supporting copy |
| `imageUrl` | string | Cloudinary secure URL of the hero image |
| `ctaText` | string \| null | CTA button label, or `null` to render no button |
| `sortOrder` | int | Display position |
| `ctaTarget` | string | Always `"/shop"` |

The public response deliberately omits the Cloudinary `publicId`, the `isActive` flag, and the
`createdAtUtc`/`updatedAtUtc` audit fields.

**CTA destination is fixed.** `ctaTarget` is a constant, not per-slide configuration, and there is
no URL field anywhere in the request or response models â€” a slide cannot be pointed at an arbitrary
site. The storefront should navigate to `ctaTarget`.

Only active slides that have an image are returned. Ordering is `sortOrder` ascending, then creation
time, then id, so slides sharing a display order always come back in the same sequence.

---

## Categories

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/api/v1/store/categories` | None | All active categories with product counts |
| GET | `/api/v1/store/categories/{slug}` | None | Category by slug |
| GET | `/api/v1/categories` | **None** | All categories (admin view), public |
| GET | `/api/v1/categories/{slug}` | **None** | One category by slug, public |
| POST | `/api/v1/categories` | Admin | Create category |
| PUT | `/api/v1/categories/{id}` | Admin | Update category (`isActive` is optional; omit it to preserve the current state) |
| PUT | `/api/v1/categories/{id}/image` | Admin | Upload or replace a category image |
| DELETE | `/api/v1/categories/{id}/image` | Admin | Remove a category image |
| DELETE | `/api/v1/categories/{id}` | Admin | Delete (fails if has children or products) |

> `GET /api/v1/categories` and `GET /api/v1/categories/{slug}` carry **no** `[Authorize]` — they
> are public. Only the write routes require `AdminOnly`.

---

## Brands

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/api/v1/store/brands` | None | All active brands with product counts |
| GET | `/api/v1/store/brands/{slug}` | None | Brand by slug |
| GET | `/api/v1/brands` | **None** | All brands (admin view), public |
| GET | `/api/v1/brands/{slug}` | **None** | One brand by slug, public |
| POST | `/api/v1/brands` | Admin | Create brand |
| PUT | `/api/v1/brands/{id}` | Admin | Update brand (`isActive` is optional; omit it to preserve the current state) |
| PUT | `/api/v1/brands/{id}/logo` | Admin | Upload or replace the brand logo |
| DELETE | `/api/v1/brands/{id}/logo` | Admin | Remove the brand logo |
| DELETE | `/api/v1/brands/{id}` | Admin | Delete (fails if has products) |

> As with categories, the two read routes (`/api/v1/brands`, `/api/v1/brands/{slug}`) are
> **public** — no `[Authorize]` attribute. The logo routes require `AdminOnly`.

---

## Products

The storefront reads `/api/v1/store/...` (anonymous). The admin catalogue lives on
`/api/v1/products` — note that `GET /api/v1/products` and `GET /api/v1/products/{slug}` are
**public**, while everything else on that prefix requires `AdminOnly`.

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/api/v1/store/products` | None | Paginated storefront catalogue |
| GET | `/api/v1/store/products/{slug}` | None | Product detail by slug (includes variants, images, rating) |
| GET | `/api/v1/store/products/{slug}/related` | None | Related products |
| GET | `/api/v1/store/featured` | None | Featured products |
| GET | `/api/v1/products` | **None** | Paginated catalogue, public |
| GET | `/api/v1/products/{slug}` | **None** | Product detail **by slug**, public |
| GET | `/api/v1/products/admin` | Admin | Paginated admin catalogue |
| GET | `/api/v1/products/admin/{id}` | Admin | Product detail **by id** — use this for the admin edit screen |
| POST | `/api/v1/products` | Admin | Create product → `201` |
| PUT | `/api/v1/products/{id}` | Admin | Update product |
| POST | `/api/v1/products/{id}/publish` | Admin | Draft → Published, `204` |
| POST | `/api/v1/products/{id}/unpublish` | Admin | Published → Draft, `204` |
| POST | `/api/v1/products/{id}/archive` | Admin | Published → Archived, `204` |

> **There is no `DELETE /api/v1/products/{id}`.** The controller exposes no delete route.
> To retire a product use `POST …/archive`; archived products stay in the database and remain
> visible in the admin catalogue with `status = "Archived"`.
>
> **Admin detail is `GET /api/v1/products/admin/{id}`, not `/api/v1/products/{id}`.**
> `{id}` and `{slug}` are different lookups on the same prefix — the slug route is public.

**Lifecycle:** `Draft` → `publish` → `Published` → `unpublish` → `Draft`, or
`Published` → `archive` → `Archived`. Only `Published` products appear in storefront responses.
A transition that is a no-op (e.g. publishing an already-published product) still returns `204`.

### Query parameters

**Storefront** — `StorefrontProductQueryRequest`:

| Parameter | Type | Default | Notes |
|-----------|------|---------|-------|
| `page` | integer | `1` | |
| `pageSize` | integer | `20` | |
| `search` | string | null | |
| `categorySlug` | string | null | **Slug**, not an id |
| `brandSlug` | string | null | **Slug**, not an id |
| `minPrice` / `maxPrice` | decimal | null | |
| `isFeatured` | bool? | null | |
| `inStockOnly` | bool | `false` | |
| `attributeFilters` | array | null | `{ "attributeName": "Colour", "attributeValue": "Black" }` — repeatable |
| `sortBy` | string | null | `name`, `price`, `created_at`, `updated_at` |
| `sortDirection` | string | `desc` | `asc` \| `desc` |

**Admin** — `ProductQueryRequest`: same shape **except** it filters by `categoryId` and
`brandId` (**GUIDs**, not slugs) and has `inStockOnly` as a nullable bool.

> `sortBy` takes a **field name**, not a compound token. Send `sortBy=price&sortDirection=asc`.
> There is no `price_asc` / `price_desc` / `newest` form. An unrecognised `sortBy` is rejected
> with `400 INVALID_SORT_FIELD` rather than silently falling back to the default ordering.

**Product fields:** `name`, `slug`, `sku?`, `price`, `compareAtPrice?`, `description?`,
`shortDescription?`, `categoryId?`, `brandId?`, `isFeatured`, `isTaxable`, `metaTitle?`,
`metaDescription?`, `metaKeywords?`. `UpdateProductRequest` has no optional defaults — send the
full object.

> **Prices are server-authoritative.** A variant's effective price is `priceOverride ?? product.price`
> and is returned pre-computed (`effectivePrice`). Never send or render a price the client
> calculated.

Errors: `400` validation, `404 PRODUCT_NOT_FOUND` / `CATEGORY_NOT_FOUND` / `BRAND_NOT_FOUND`,
`409` duplicate slug or SKU.

---

## Product Images â€” Admin

All routes require `AdminOnly`.

| Method | Route | Description |
|--------|-------|-------------|
| POST | `/api/v1/products/{productId}/images` | Upload one or more images (multipart) |
| PUT | `/api/v1/products/{productId}/images/reorder` | Reorder the gallery |
| PUT | `/api/v1/products/{productId}/images/{imageId}/set-primary` | Set the primary image |
| DELETE | `/api/v1/products/{productId}/images/{imageId}` | Delete an image |

> The set-primary path segment is **`set-primary`**, not `/primary`. Use this exact spelling.

**Upload** accepts `multipart/form-data` with an `IFormFileCollection` bound to field `files`,
plus an optional `altText` form field. Maximum 10 files per request, 10 MB per file. Accepted
MIME types: `image/jpeg`, `image/png`, `image/webp`, `image/gif`, `image/avif`.

Returns `201` with an array of `ProductImageDto`. The first image uploaded for a product
automatically becomes primary regardless of the `isPrimary` flag you send.

Errors: `400 EMPTY_FILE`, `400 INVALID_MIME_TYPE`, `400 TOO_MANY_FILES`, `502 UPLOAD_FAILED`
(Cloudinary rejected the file), `404 PRODUCT_NOT_FOUND`.


---

## Product Variants â€” Admin

All routes require `AdminOnly` and are nested under their parent product.
`productId` is a **GUID**; `variantId` is a **GUID**.

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/v1/products/{productId}/variants` | List all variants with stock levels |
| GET | `/api/v1/products/{productId}/variants/{variantId}` | Get one variant |
| POST | `/api/v1/products/{productId}/variants` | Create variant (auto-creates its inventory row at 0) |
| PUT | `/api/v1/products/{productId}/variants/{variantId}` | Update variant fields and active state |
| DELETE | `/api/v1/products/{productId}/variants/{variantId}` | Delete variant + its inventory row |

Responses: `200` (GET/PUT), `201` (POST), `204` (DELETE).
Errors: `404` `PRODUCT_NOT_FOUND` / `VARIANT_NOT_FOUND`, `400` validation, `409`
`VARIANT_DUPLICATE_COMBINATION` / duplicate SKU.

### `VariantResponse`

```json
{
  "id": "â€¦",
  "sku": "TSHIRT-BLK-L",
  "priceOverride": 549.00,
  "sortOrder": 0,
  "isActive": true,
  "attributeValueIds": "9f1câ€¦,3ab2â€¦",
  "availableStock": 12,
  "attributes": [
    { "attributeValueId": "9f1câ€¦", "attributeId": "4de5â€¦", "attributeName": "Colour", "value": "Black" },
    { "attributeValueId": "3ab2â€¦", "attributeId": "7a18â€¦", "attributeName": "Size",   "value": "Large" }
  ],
  "isOutOfStock": false
}
```

| Field | Type | Notes |
|-------|------|-------|
| `sku` | string \| null | Globally unique across all variants when set |
| `priceOverride` | decimal \| null | `null` â†’ inherits the product's base price |
| `sortOrder` | integer | Display order |
| `isActive` | boolean | Inactive variants are hidden from the storefront |
| `attributeValueIds` | string \| null | Legacy storage format: comma-separated, sorted GUIDs. Prefer `attributes`. |
| `availableStock` | integer \| **null** | `null` means the variant has **no inventory row yet** â€” treat as unknown, not as zero |
| `attributes` | array \| null | Resolved, display-ready attribute values (see below) |
| `isOutOfStock` | boolean | Computed: `availableStock != null && availableStock <= 0` |

> **`availableStock` is nullable.** It is `null` until an inventory row exists for the variant,
> and `isOutOfStock` is therefore `false` for a `null` value. If your UI needs to distinguish
> "no stock configured" from "zero stock", test `availableStock == null` explicitly rather than
> relying on `isOutOfStock`.

`attributes` is the resolved view that lets a variant selector render without a second round
trip â€” each entry pairs a value with its parent attribute's id and name.

### `POST /api/v1/products/{productId}/variants` â€” `CreateVariantRequest`

```json
{
  "sku": "TSHIRT-BLK-L",
  "priceOverride": 549.00,
  "sortOrder": 0,
  "attributeValueIds": ["9f1câ€¦", "3ab2â€¦"]
}
```

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `sku` | string \| null | no | Unique when provided |
| `priceOverride` | decimal \| null | no | `null` inherits product price |
| `sortOrder` | integer \| null | no (default null) | |
| `attributeValueIds` | GUID[] \| null | no | One value per attribute axis |

### `PUT /api/v1/products/{productId}/variants/{variantId}` â€” `UpdateVariantRequest`

```json
{
  "sku": "TSHIRT-BLK-L",
  "priceOverride": 549.00,
  "sortOrder": 0,
  "isActive": true,
  "attributeValueIds": ["9f1câ€¦", "3ab2â€¦"]
}
```

`sortOrder` and `isActive` are **required and not defaulted** â€” send the full current object.
A partial update that omits them will fail validation, and omitting `isActive` will not preserve
the existing value.

### Server-enforced validation

- SKU is globally unique across all variants when provided.
- Every `attributeValueId` must belong to an attribute **defined on this product**.
- At most one value per attribute â€” a variant cannot be both `Colour:Black` and `Colour:White`.
- The combination is **order-insensitive**: `[Colour:Black, Size:Large]` â‰¡ `[Size:Large, Colour:Black]`.
- A duplicate combination is rejected with `409 VARIANT_DUPLICATE_COMBINATION`.
- `priceOverride >= 0`, `sortOrder >= 0`.

### Stock interaction

Creating a variant auto-creates an `InventoryItem` at `onHand = 0`, so `availableStock` returns
`0` (not `null`) immediately. Set real stock with:

```
PUT /api/v1/admin/inventory/{productId}?variantId={variantId}
```

Deleting a variant also removes its inventory row. Existing order snapshots are unaffected â€”
order items store immutable copies of name and price.

> Variants have **no public admin-independent route.** For storefront variant data, read
> `variants` from `GET /api/v1/store/products/{slug}`, which returns
> `StorefrontVariantResponse` (`effectivePrice`, `stockAvailability`, `canPurchase` â€” and no raw
> stock counts).

---

## Product Attributes â€” Admin

Defines the axes a product varies on (Colour, Size, Storage, â€¦) and the selectable values for
each. A variant then references one value per axis â€” this is what identifies a variant.

All routes require `AdminOnly`.

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/v1/products/{productId}/attributes` | List attributes and their values |
| PUT | `/api/v1/products/{productId}/attributes` | Create or update one attribute (upsert) |
| DELETE | `/api/v1/products/{productId}/attributes/{attributeId}` | Delete an attribute |

### `PUT /api/v1/products/{productId}/attributes` â€” `UpsertProductAttributeRequest`

```json
{
  "name": "Colour",
  "values": [
    { "value": "Black" },
    { "value": "White" },
    { "id": "7a18-â€¦-4c2f", "value": "Navy" }
  ]
}
```

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `name` | string | yes | Attribute name, unique per product |
| `values` | array | yes | Each entry: `value` (required), `id` (optional â€” omit to create, include to keep an existing value) |
| `values[].id` | GUID \| null | no | Present on update to preserve existing value identities |

This is an **upsert keyed by attribute name**: `PUT` with a new name creates the attribute;
`PUT` with an existing name replaces its value list. Values supplied without an `id` are created.

> Removing a value from `values` that a variant still references will invalidate that variant.
> Delete the value deliberately and expect the affected variant to be rejected or orphaned â€”
> validate with `GET /api/v1/products/{productId}/variants` before pruning.

### Responses

`GET` and `PUT` both return `ProductAttributesResponse`:

```json
{
  "productId": "â€¦",
  "attributes": [
    {
      "id": "4de5-â€¦-9a1b",
      "name": "Colour",
      "sortOrder": 0,
      "values": [ { "id": "9f1c-â€¦-77d1", "value": "Black", "sortOrder": 0 } ]
    }
  ]
}
```

`DELETE` returns `204`.
---

## Inventory â€” Admin

Stock is managed per **product**, optionally narrowed to one **variant**. All routes require
`AdminOnly` and live under `/api/v1/admin/inventory`. There is **no** `GET` route â€” stock is
returned as the response body of the two write routes, and is otherwise read through the product
detail and storefront responses.

| Method | Route | Description |
|--------|-------|-------------|
| PUT | `/api/v1/admin/inventory/{productId}?variantId=` | Set absolute on-hand quantity (creates the record if absent) |
| POST | `/api/v1/admin/inventory/{productId}/adjust?variantId=` | Apply a signed delta to on-hand |

Both routes take `variantId` as an **optional query parameter**:
- omitted â†’ operates on the **base product** inventory row.
- provided â†’ operates on that specific variant's row.

### PUT `/api/v1/admin/inventory/{productId}`

Request â€” `SetStockRequest`:

```json
{ "onHand": 25, "lowStockThreshold": 5 }
```

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `onHand` | integer | yes | Absolute replacement, not a delta |
| `lowStockThreshold` | integer | no (default `5`) | At or below this, `isLowStock` is true |

### POST `/api/v1/admin/inventory/{productId}/adjust`

Request â€” `AdjustStockRequest`:

```json
{ "delta": -3, "reason": "Damaged in transit" }
```

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `delta` | integer | yes | Positive restocks, negative reduces |
| `reason` | string \| null | no | Free text, audit context |

### Response â€” `InventoryResponse` (identical for both routes)

```json
{
  "id": "â€¦",
  "productId": "â€¦",
  "variantId": null,
  "onHand": 22,
  "reserved": 3,
  "available": 19,
  "lowStockThreshold": 5,
  "isLowStock": false,
  "isOutOfStock": false,
  "updatedAt": "2026-10-02T07:00:00Z"
}
```

**Stock model â€” read this before building any stock UI:**

| Field | Meaning |
|-------|---------|
| `onHand` | Physical units in stock. Admin-only. |
| `reserved` | Units held by carts/checkouts that have not completed. Admin-only. |
| `available` | `onHand - reserved`. **This is the number a customer can actually buy.** |
| `isLowStock` | `available <= lowStockThreshold` |
| `isOutOfStock` | `available <= 0` |

**Where stock can be read:**

| Source | Fields exposed |
|--------|----------------|
| `PUT` / `POST /api/v1/admin/inventory/...` response | `onHand`, `reserved`, `available`, `isLowStock`, `isOutOfStock` |
| Admin product detail (`GET /api/v1/products/admin/{id}`) | `variants[].availableStock` only — **no** product-level `onHand`/`reserved` |
| Storefront responses (`/store/products…`, `ProductReviews`, wishlist) | `stockAvailability` (`InStock` \| `LowStock` \| `OutOfStock`) and `canPurchase` only |

`onHand`, `reserved` and `available` are therefore available **only** from the two inventory
routes. Admin product detail gives you a variant's `availableStock` (see
[Product Variants](#product-variants--admin)) but never the raw counters. Public storefront
responses expose only the availability enum — never counts.

This matters for admin stock screens: to edit a base product's quantity, you need the inventory
routes, not the product detail response.

> Why the asymmetry: `onHand` and `reserved` are internal inventory counters that a concurrent
> checkout reservation can change at any moment. Gating customer UI on them produces
> "add to cart failed" screens. `availableStock` on an admin variant response and
> `stockAvailability` on storefront responses are the two sanctioned shapes.

On-hand cannot go negative. Reserved units cannot be removed by setting stock until the
reservation is released. Writes are concurrency-safe via the PostgreSQL `xmin` system column.

Errors: `PRODUCT_NOT_FOUND`, `INVENTORY_NOT_FOUND`, `STOCK_ADJUSTMENT_INVALID`.

Server-enforced stock rules (these are domain invariants, not just validation):
- `onHand` cannot go negative.
- `onHand` cannot be set below the currently `reserved` amount â€” reduce below reserved and the
  write is rejected.
- The same floor applies to a negative `delta`.

---

## Cart

All routes accept an anonymous caller; the cart is resolved per owner as described below.

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/api/v1/cart` | None | Get current cart → `CartResponse` |
| POST | `/api/v1/cart/items` | None | Add item → `CartResponse` |
| PUT | `/api/v1/cart/items/{itemId}` | None | Update quantity → `CartResponse` |
| DELETE | `/api/v1/cart/items/{itemId}` | None | Remove item → `204` |
| DELETE | `/api/v1/cart` | None | Clear cart → `204` |
| POST | `/api/v1/cart/coupon` | **Customer** | Apply coupon → `CheckoutSummaryResponse` |
| DELETE | `/api/v1/cart/coupon` | **Customer** | Remove coupon → `CheckoutSummaryResponse` |

**Anonymous carts:** send the `X-Cart-Token` header (max 64 chars) with the server-issued
token, returned in the first `AddCartItem` response.
**Authenticated carts:** resolved from the JWT `sub` claim — no token needed.

> **Coupons require an authenticated customer.** Both coupon routes return `401` for an
> anonymous `X-Cart-Token` cart, even though every other cart route accepts one. An anonymous
> cart can hold items but cannot carry a discount.

Adding the same product+variant twice merges into one line and increments the quantity rather
than creating a second row. Errors: `404 PRODUCT_NOT_FOUND` / `VARIANT_NOT_FOUND`,
`409` for a deleted/unavailable product, `400` for a non-positive quantity.

> **The cart stores product and variant ids only — never prices.** Checkout recalculates every
> amount from the database, so a cart response can show a stale price until `GET /checkout/summary`
> is called. Treat cart totals as indicative and the checkout summary as authoritative.

---

## Checkout

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/api/v1/checkout/summary` | None | Live totals for the current cart |
| POST | `/api/v1/checkout` | Customer | Place the order |
| POST | `/api/v1/payments/verify` | Customer | Verify Razorpay payment after the widget |

`GET /api/v1/checkout/summary` works for anonymous carts too (same `X-Cart-Token` rule), and
returns `CheckoutSummaryResponse` — the same shape the coupon routes return. Use it to refresh
totals after any cart or coupon mutation rather than recomputing them client-side.

**Checkout request:** `{ addressId, paymentMethod, couponCode?, idempotencyKey? }`

`addressId` must be the id of a saved address belonging to the authenticated customer. Checkout
snapshots that address into the order, so later edits do not alter order history.
`paymentMethod`: `"Razorpay"` or `"CashOnDelivery"`.

**Response includes server-calculated:** `subtotal`, `shippingAmount`, `codFee`,
`discountAmount`, `taxAmount`, `grandTotal`. Never trust client totals.

**Order of operations:**
```
subtotal - discount = taxableBase
taxableBase + tax (exclusive) + shipping + codFee = grandTotal
```
For inclusive tax, tax is extracted from subtotal:
`grandTotal = subtotal - discount + shipping + codFee`.

**Checkout request:** `{ addressId, paymentMethod, couponCode?, idempotencyKey? }`
`addressId` must be the ID of a saved address belonging to the authenticated customer. Checkout snapshots that address into the order, so later edits do not alter order history.
`paymentMethod`: `"Razorpay"` or `"CashOnDelivery"`

**Checkout response includes server-calculated:** `subtotal`, `shippingAmount`, `codFee`, `discountAmount`, `taxAmount`, `grandTotal`. Never trust client totals.

**Order of operations:**
```
subtotal - discount = taxableBase
taxableBase + tax (exclusive) + shipping + codFee = grandTotal
```

For inclusive tax: tax is extracted from subtotal; grand total = subtotal - discount + shipping + codFee.

---

## Orders â€” Customer

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/api/v1/orders` | Customer | Paginated order history |
| GET | `/api/v1/orders/{id}` | Customer | Order detail (customer-scoped) |
| POST | `/api/v1/orders/{id}/cancel` | Customer | Cancel order (if cancellable) |

Customer can only access their own orders. `CustomerId` comes from JWT, never from request body.

---

## Orders â€” Admin

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/api/v1/admin/orders` | Admin | Paginated orders with filters |
| GET | `/api/v1/admin/orders/{id}` | Admin | Any order detail |
| PUT | `/api/v1/admin/orders/{id}/status` | Admin | Set order status |

> **There is no `POST /api/v1/admin/orders/{id}/cancel`.** The admin order controller exposes only
> three routes: list, get-by-id, and set-status. To cancel an order as an admin, use
> `PUT /api/v1/admin/orders/{id}/status` with the target status.

**Admin order filters:** `search`, `status`, `fromDate`, `toDate`, `sortBy`, `sortDirection`, `page`, `pageSize`.

---

## Promotions â€” Admin

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/api/v1/admin/promotions` | Admin | Paginated promotions |
| GET | `/api/v1/admin/promotions/{id}` | Admin | Promotion detail |
| POST | `/api/v1/admin/promotions` | Admin | Create promotion |
| PUT | `/api/v1/admin/promotions/{id}` | Admin | Update promotion |
| POST | `/api/v1/admin/promotions/{id}/activate` | Admin | Activate |
| POST | `/api/v1/admin/promotions/{id}/deactivate` | Admin | Deactivate |
| DELETE | `/api/v1/admin/promotions/{id}` | Admin | Delete (inactive + unused only) |

`discountType`: `"Percentage"` or `"FixedAmount"`.  
`applicability`: `"EntireOrder"`, `"SpecificProducts"`, or `"SpecificCategories"`.

---

## Promotions â€” Customer

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| POST | `/api/v1/store/promotions/validate` | Customer | Validate coupon against current cart |

**Request:** `{ couponCode }` â€” server recalculates cart from database, never trusts client subtotal.  
**Note:** A valid response here does NOT guarantee the coupon remains valid at checkout. Checkout re-validates.

---

## Customer Profile

All routes require an authenticated customer (`[Authorize]`, any valid JWT).

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/v1/me` | Current user identity (id, email, role) |
| GET | `/api/v1/customer/profile` | Customer profile |
| PUT | `/api/v1/customer/profile` | Update profile |

> Profile lives on **`/api/v1/customer/profile`**, not `/api/v1/me/profile`. `/api/v1/me` is a
> separate, read-only identity route.
>
> There is **no avatar upload endpoint.** No `POST /api/v1/me/profile/avatar` exists.

`UpdateCustomerProfileRequest` carries the editable profile fields; send only what changes.

---

## Customer Addresses

All routes require `CustomerOrAdmin`.

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/v1/customer/addresses` | All addresses for the caller |
| GET | `/api/v1/customer/addresses/{id}` | One address |
| POST | `/api/v1/customer/addresses` | Create address |
| PUT | `/api/v1/customer/addresses/{id}` | Update address |
| DELETE | `/api/v1/customer/addresses/{id}` | Delete address |
| PUT | `/api/v1/customer/addresses/{id}/default` | Set as default |

> Addresses live on **`/api/v1/customer/addresses`**, not `/api/v1/me/addresses`.
> The set-default route is **`PUT` …`/default`**, not `POST` …`/set-default`.

**`CreateAddressRequest`:** `label?`, `firstName`*, `lastName`*, `company?`, `addressLine1`*,
`addressLine2?`, `city`*, `state`*, `postalCode`*, `countryCode`*, `phone?`, `isDefault`
(default `false`).

Every address is scoped to the authenticated caller in the query itself, so one customer cannot
read or mutate another's addresses even by guessing an id.

**Default-address behaviour worth knowing:**
- `isDefault = true` on create clears the previous default in the same save.
- Deleting the default address automatically promotes the most recently created remaining
  address to default. If no addresses remain, there is simply no default.
- Setting a new default clears the old one transactionally.

> `phone` is optional. When omitted, the address inherits the customer's **verified** account
> phone number. An unverified number is never used as the default, so checkout's phone
> verification requirement cannot be satisfied by a self-declared value.

Errors: `404 ADDRESS_NOT_FOUND`.

---

## Integration Config â€” Admin

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/api/v1/admin/integrations/payment` | Admin | Razorpay status (masked, no secrets) |
| PUT | `/api/v1/admin/integrations/payment` | Admin | Update Razorpay config |
| GET | `/api/v1/admin/integrations/google` | Admin | Google OAuth status |
| PUT | `/api/v1/admin/integrations/google` | Admin | Update Google OAuth config |
| GET | `/api/v1/admin/integrations/email` | Admin | Brevo email status |
| PUT | `/api/v1/admin/integrations/email` | Admin | Update Brevo config |
| GET | `/api/v1/admin/integrations/sms` | Admin | SMS provider status |
| PUT | `/api/v1/admin/integrations/sms` | Admin | Select provider + credentials. Returns the updated status (`200`), not `204` |

**Security:** Secrets are never returned. Responses contain `isConfigured` flags and masked
identifiers only.

### SMS Providers & Templates — Admin

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/v1/admin/integrations/sms/providers` | **Field schema per provider — render your form from this** |
| GET | `/api/v1/admin/integrations/sms/templates` | All SMS templates |
| POST | `/api/v1/admin/integrations/sms/templates` | Create a template → `201` |
| PUT | `/api/v1/admin/integrations/sms/templates/{id}` | Update a template |
| DELETE | `/api/v1/admin/integrations/sms/templates/{id}` | Delete a template → `204` |

Supported providers are `None`, `TwoFactor`, `Free2Sms`, and `Twilio`. **Exactly one provider is
active at a time**, selected via `PUT /api/v1/admin/integrations/sms` — that is the only place
provider selection happens.

#### `GET /admin/integrations/sms/providers` — the field schema

**The backend is the source of truth for the configuration form.** This endpoint returns, per
provider, exactly which inputs to render, which are required, and what format each expects. The
same table validates the save, so the form and the enforced rules cannot disagree.

```jsonc
[{
  "name": "2Factor",
  "description": "API-key SMS gateway. Sends the verification code this application generates.",
  "supportsNativeOtp": true,       // has a dedicated OTP endpoint → offer a delivery mode
  "requiresTemplate": true,        // show the template section
  "requiredSettings": ["ApiKey"],
  "settings": [
    {
      "key": "ApiKey",             // the wire key for PUT .../sms
      "label": "API key",
      "type": "Secret",            // Text | Secret | Textarea | Number | Select
      "required": true,
      "secret": true,              // write-only; never returned by any endpoint
      "advanced": false,
      "helpText": "2Factor 'secret' from Account settings. Sent as the X-API-Key header.",
      "placeholder": "Paste the key from your provider account",
      "formatHint": null,
      "maxLength": 500,
      "allowedValues": null,        // populated when type = Select
      "requiredWhen": null,
      "defaultValue": null
    }
  ],
  "templateFields": [ /* same shape; key is "externalTemplateId" or "body" */ ],
  "notes": ["The template name is sent on every request, so changing it takes effect immediately."]
}]
```

**Field keys differ by gateway for the same vendor concept.** The UI must use `label`, not a
hard-coded string:

| Wire key | 2Factor | Free2SMS | Twilio |
|----------|---------|----------|--------|
| `externalTemplateId` | **Template name** (e.g. `LOGIN_OTP`) — **required** | DLT template ID — optional | Verify Template SID (`HJ…`) — optional |
| `body` | optional | optional | optional |

**Render rules:**

- Show only `settings` for the selected provider. Switching provider and saving **clears** every
  field that does not apply to the new provider — the save replaces the stored set, so a 2Factor
  `OtpPath` is not carried into Free2SMS.
- Collapse `advanced: true` fields behind a disclosure. They are fully configurable and validated.
- Render `type: "Secret"` as a password input and never pre-fill it; those values are never
  returned by any endpoint.
- Render `type: "Select"` with `allowedValues`.
- Mark `required: true` as mandatory. Where `requiredWhen` is set instead, the field becomes
  mandatory under that condition — the 2Factor template name is required whenever the
  transactional template route is used.
- Hide the whole template section when `requiresTemplate` is false.
- Only offer a delivery mode when `supportsNativeOtp` is true.

**Server-side enforcement (mirrors the schema exactly):**

- Unknown setting keys → `400`, naming the accepted ones.
- Missing `requiredSettings` while `enabled: true` → `400`.
- Value beyond `maxLength`, outside `allowedValues`, or a path not starting with `/` → `400`.
- A 2Factor template saved without `externalTemplateId` → `400 SMS_TEMPLATE_INVALID`. This was
  previously accepted and failed much later at send time with `TEMPLATE_NOT_CONFIGURED`.


- `SmsTemplateResponse`: `id`, `provider`, `name`, `body`, `externalTemplateId?`, `isActive`,
  `updatedAtUtc`.
- `CreateSmsTemplateRequest`: `provider`*, `name`*, `body?`, `externalTemplateId?`,
  `isActive` (default `true`).
- `UpdateSmsTemplateRequest`: `name`*, `body?`, `externalTemplateId?`, `isActive`* — no defaults,
  send the full object.

> Provider selection does **not** live on `PUT /api/v1/admin/settings/auth`; that endpoint no
> longer accepts an `smsProvider` field. Saving OTP policy there without a provider is a `200`.

> The database `SmsProviderConfig` row is authoritative. Deployment-level `SmsOptions` are only
> read as a bootstrap when no row exists yet.

#### OTP delivery: which route each provider uses

Every send attempts the provider's **native OTP endpoint** first and only falls back to the
**transactional template** route. `deliveryMode` selects the behaviour explicitly:

| Value | Behaviour |
|-------|-----------|
| `Auto` | Native OTP first; transactional only when the gateway reports the native route is unavailable (`404`/`405`/`501`). |
| `NativeOtp` | Native route only. Never falls back. |
| `TransactionalTemplate` | Always sends a code **this application generated**, through the registered template. |

> **Why the fallback is deliberately narrow.** Fallback happens *only* when the gateway says the
> route does not exist. A timeout, `5xx` or rate limit is ambiguous — the first SMS may already
> have been delivered — so a second send could cost money and leave the customer holding one of two
> codes. Those are reported as retryable instead.

> **Who verifies the code.** `NativeOtp` for a hosted-OTP gateway means the **vendor** generates and
> validates the code, so expiry, hashing, attempt limits and resend cooldown move to the vendor and
> a customer meets different verification behaviour depending on the active gateway. Use
> `TransactionalTemplate` to keep this application authoritative. This is the one setting where the
> default (`Auto`) is chosen for delivery robustness, not for uniformity.

#### What to capture per provider

`PUT /api/v1/admin/integrations/sms` takes a `settings` object keyed by name. **Only `credentials`
are secrets**; every route/tuning setting below is safe to store and is never returned by a GET.

**2Factor** (`provider: "2Factor"`) — required: `ApiKey`.

| Setting | Required | Default | Notes |
|---------|----------|---------|-------|
| `ApiKey` | yes | — | Sent as `X-API-Key`. Set `ApiKeyHeader` empty to send it as `apiKey` in the body instead. |
| `DeliveryMode` | no | `Auto` | `Auto` / `NativeOtp` / `TransactionalTemplate`. |
| `OtpPath` | no | `/API/V1/OTP/SEND` | Native OTP route. |
| `TransactionalPath` | no | `/sms/{apiKey}/{template}` | Supports `{apiKey}` and `{template}` placeholders. |
| `TemplateNameField` | no | `template_name` | Body field carrying the template name. |
| `OtpVariableName` | no | `var1` | Template variable receiving the code. |
| `ExpiryVariableName` | no | `var2` | Only sent when the body uses `{EXPIRY_MINUTES}`. |
| `ApiKeyHeader` | no | `X-API-Key` | Empty ⇒ key travels in the body. |
| `Channel` | no | `SMS` | `SMS` / `VOICE` / `auto`. |
| `SenderId` | no | — | Approved sender id on the account. |
| `BaseUrl` | no | `https://2factor.in` | |
| `SendPath` | no | — | Legacy alias honoured for `OtpPath`; kept so existing rows keep working. |

**The template name is read from the template row on every request**, not from configuration. It is
the template's `externalTemplateId` — for 2Factor that field holds the approved **template name**
(e.g. `LOGIN_OTP`), not a numeric id. Body placeholders `{OTP}` and `{EXPIRY_MINUTES}` become
`OtpVariableName`/`ExpiryVariableName` variables.

> **2Factor documentation caveat.** 2Factor publishes more than one generation of this API and its
> own pages disagree on the template field name (`template_name`, `template`, `templateName`). Its
> machine-readable reference is a JavaScript-rendered page that cannot be read programmatically.
> Every divergent name is therefore a setting above, so an account can be corrected from this API
> without a rebuild — which is what makes sandbox testing possible.

**Twilio** (`provider: "Twilio"`) — required: `AccountSid`, `AuthToken`, `ServiceSid`.

| Setting | Required | Default | Notes |
|---------|----------|---------|-------|
| `AccountSid` | yes | — | `AC…` |
| `AuthToken` | yes | — | Secret. |
| `ServiceSid` | yes | — | `VA…` Verify Service. |
| `DeliveryMode` | no | `NativeOtp` | Verify *is* Twilio's native OTP endpoint. |
| `MessagingServiceSid` | no | — | `MG…`; required by Twilio unless `SenderId` is set. |
| `SenderId` | no | — | Used only by the Programmable Messaging fallback. |
| `MessagingBaseUrl` | no | `https://api.twilio.com` | Fallback host. |
| `MessagingPath` | no | `/2010-04-01/Accounts/{accountSid}/Messages.json` | Fallback route. |
| `BaseUrl` | no | `https://verify.twilio.com` | |

Template handling follows Twilio's documented precedence: request `TemplateSid` → Service
`DefaultTemplateSid` → Verify default. The template's `externalTemplateId` holds the `TemplateSid`
(`HJ…`). Placeholders in the body are passed as `TemplateCustomSubstitutions`; a raw message body
is never sent (Twilio rejects it with error `60243`).

> `CustomCodeEnabled` must be on the Verify Service to use a bring-your-own code.

**Free2Sms** (`provider: "Free2Sms"`) — required: `ApiKey`, `SenderId`. **No native OTP endpoint
is published**, so this gateway always uses the transactional DLT route.

| Setting | Required | Default | Notes |
|---------|----------|---------|-------|
| `ApiKey` | yes | — | Sent as `Authorization: Bearer`. |
| `SenderId` | yes | — | DLT-approved 6-character header. |
| `Route` | no | `otp` | Priority queue; correct for authentication codes. |
| `DeliveryMode` | no | `TransactionalTemplate` | Fixed for this gateway. |
| `BaseUrl` | no | `https://free2sms.com/api/v1` | |

The template's `externalTemplateId` is the **numeric DLT template id** (stored as text because a
registration can exceed `Int64`); a non-numeric value is dropped and Free2SMS falls back to
content matching. The body must match the approved template exactly or the send is rejected with
`TEMPLATE_MISMATCH` before being billed.

> **Brevo is not an SMS provider here.** Brevo is wired for **email** only
> (`/admin/integrations/email`). Brevo's transactional SMS accepts an integer `templateId` plus a
> `params` map, which is a different contract; adding it as an SMS gateway would mean a new
> `SmsProviderKind` value.

#### OTP send auditing

Every send attempt — including each leg of a fallback — writes one structured audit record via
`ISmsOtpAuditSink` (default: structured logging). Each record carries `provider`, `mode`,
`attemptedRoute`, `usedFallback`, `templateName`, `templateReference`, `maskedPhone`, `success`,
`providerMessageId`, `errorCode`, `retryable` and `durationMs`.

> **Never included:** the OTP, any credential, the full destination number, or the rendered message
> body. Records carry `...3210`-style masked numbers only, because an audit trail is retained for
> compliance and read by people who must not be able to complete a login.

**Cash on delivery:** COD is **not** configured here. It is a shipping concern and has exactly one
configuration path: `PUT /api/v1/admin/settings/delivery`, which sets `codEnabled` and
`codExtraFee` together. The former `PUT /api/v1/admin/integrations/payment/cod` endpoint has been
**removed** â€” it created a second surface for one switch, which is what let the Integrations and
Shipping screens disagree about availability. New stores have COD disabled by default.

**Tracking IDs** (GA4, Meta Pixel) are configured via environment variables (`Tracking__GoogleAnalyticsMeasurementId`, `Tracking__MetaPixelId`) â€” no admin API endpoint needed. They appear in the public `/store/settings` response.

---

## Payments â€” Webhook

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| POST | `/api/v1/payments/webhook` | None (HMAC signed) | Razorpay webhook handler |

Razorpay sends a signed webhook body. The handler verifies the HMAC-SHA256 signature against `Razorpay__WebhookSecret` before any processing.  
**Idempotent** â€” duplicate webhooks with the same `ProviderEventId` are silently ignored (unique index on `WebhookEvents`).  
**Atomic** â€” payment state + `WebhookEvent` insert happen in a single transaction; a concurrent duplicate hitting the unique constraint returns 200 safely.  
**Never trust** payment status from the webhook body alone â€” it is only acted on after signature verification.

---

## OTP

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| POST | `/api/v1/otp/send` | None | Send OTP to phone number |
| POST | `/api/v1/otp/verify` | None | Verify OTP and authenticate |
| GET | `/api/v1/otp/verification-status` | **Any JWT** | Whether the caller's phone is verified |

Rate-limited: OTP policy (configurable, default 5/60s).

`GET /api/v1/otp/verification-status` is **not** anonymous — it requires a valid token because it
reports state about the authenticated caller. Call it to decide whether to show a
"verify your phone" prompt before checkout.

---

## Health

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/v1/health` | Health check (also answers `HEAD`) |

> The versioned path is `/api/v1/health`. There is **no `/health/ready`** endpoint, and no
> separate liveness/readiness split — the single route reports overall service health.

Health responses never expose connection strings, credentials, or stack traces.

---

## Customer Wishlist

All routes require a customer (or admin) JWT. The customer id is taken from the access token on
every route and is **never** read from a body, query, or route value.

Delete routes are keyed on `productId` + `productVariantId` rather than on the wishlist row id, so
one customer cannot act on another account's entries.

| Method | Route | Result |
|--------|-------|--------|
| GET | `/api/v1/wishlist?page=&pageSize=` | `200` `PagedResponse<WishlistItemResponse>` |
| GET | `/api/v1/wishlist/status?productIds=` | `200` `WishlistStatusResponse` |
| POST | `/api/v1/wishlist` | `201` created, `200` already saved |
| DELETE | `/api/v1/wishlist/{productId}?productVariantId=` | `204` |
| DELETE | `/api/v1/wishlist` | `204` |

**`POST /api/v1/wishlist`** is **idempotent**. Adding an item that is already saved returns `200`
with `"created": false` rather than `409`, so a double-tapped heart is a UI accident, not an error.

```json
{ "productId": "â€¦", "productVariantId": null }
```

`productVariantId` is optional. Omit it to save the product; supply it to pin the entry to one
variant. The variant is immutable once saved â€” switching variants is remove-then-add.

```json
{
  "item": {
    "id": "â€¦", "productId": "â€¦", "productVariantId": null,
    "productName": "Cotton T-Shirt", "productSlug": "cotton-t-shirt",
    "productImageUrl": "https://res.cloudinary.com/â€¦",
    "basePrice": 499.00, "variantPrice": null, "effectivePrice": 499.00,
    "currencyCode": "INR",
    "stockAvailability": "InStock", "canPurchase": true,
    "isProductActive": true, "addedAtUtc": "2026-10-02T07:00:00Z"
  },
  "created": true
}
```

> `stockAvailability` is one of `InStock`, `LowStock`, `OutOfStock`. Raw `onHand` / `reserved`
> are deliberately **not** exposed â€” they are internal inventory counters that a concurrent
> checkout reservation can invalidate before the customer reaches the cart.

**`GET /api/v1/wishlist/status`** takes up to 200 `productIds` and returns the subset that is on the
wishlist. Use it to render filled/empty hearts across a whole product grid in one request.

Errors: `PRODUCT_NOT_FOUND`, `PRODUCT_NOT_AVAILABLE`, `PRODUCT_VARIANT_MISMATCH`,
`PRODUCT_VARIANT_UNAVAILABLE`, `WISHLIST_ITEM_NOT_FOUND`.

---

## Product Reviews

### Public (no authentication)

| Method | Route | Result |
|--------|-------|--------|
| GET | `/api/v1/products/{productId}/reviews?page=&pageSize=&sort=&rating=` | `200` `ReviewListResponse` |

Only `Published` reviews are ever returned. `sort` is `recent` (default), `helpful`, or `rating`;
any other value is `400`. `rating` filters to an exact star value. `pageSize` max 50.

```json
{
  "page": {
    "items": [
      {
        "id": "â€¦", "productId": "â€¦", "productVariantId": null,
        "authorName": "Ada Lovelace",
        "rating": 5, "title": "Great", "body": "Exactly as described.",
        "isVerifiedPurchase": true, "helpfulCount": 3,
        "images": [ { "id": "â€¦", "publicId": "reviews/a", "url": "https://res.cloudinary.com/â€¦", "sortOrder": 0 } ],
        "publishedAtUtc": "2026-10-02T07:00:00Z", "createdAtUtc": "2026-10-02T06:00:00Z"
      }
    ],
    "page": 1, "pageSize": 20, "totalCount": 1,
    "totalPages": 1, "hasNextPage": false, "hasPreviousPage": false
  },
  "ratingAverage": 4.5,
  "ratingCount": 2,
  "breakdown": { "byRating": { "5": 1, "4": 1 } }
}
```

The aggregate is computed from **all** published reviews, not from the page, so paging never
changes the summary block. `authorName` is first + last name, falling back to the email local
part â€” it never contains `@`. The public projection has no customer id, email, or phone field.

### Customer

| Method | Route | Result |
|--------|-------|--------|
| POST | `/api/v1/products/{productId}/reviews` | `201` `MyReviewResponse` |
| GET | `/api/v1/reviews/mine?page=&pageSize=` | `200` `PagedResponse<MyReviewResponse>` |
| PUT | `/api/v1/reviews/{reviewId}` | `200` `MyReviewResponse` |
| DELETE | `/api/v1/reviews/{reviewId}` | `204` |
| POST | `/api/v1/reviews/{reviewId}/helpful` | `200` `ReviewHelpfulResponse` |
| POST | `/api/v1/reviews/images` | `201` `ReviewImageUploadResponse` |

**`POST /api/v1/products/{productId}/reviews`**

```json
{
  "productVariantId": null,
  "rating": 5,
  "title": "Great",
  "body": "Exactly as described.",
  "images": [ { "publicId": "reviews/a", "url": "https://res.cloudinary.com/demo/image/upload/reviews/a.jpg" } ]
}
```

There is **no `isVerifiedPurchase` field, by design.** The badge is derived server-side from an
existing `Delivered` order for the customer containing the product. A client cannot grant it.

There is also no `customerId` field. Ownership comes from the access token.

Reviews are created with `Pending` status and become public only after an admin publishes them.
One review per customer per product/variant is enforced by a unique database index, so a
concurrent double-submit cannot create a duplicate â€” the loser gets `409`.

**`PUT /api/v1/reviews/{reviewId}`** edits content only. It does not change status, publish time,
verification, or ownership â€” editing a published review does **not** re-enter moderation.

**`POST /api/v1/reviews/{reviewId}/helpful`** toggles: calling it again withdraws the vote, and
the count can fall back to zero. Voting on your own review returns `400 PRODUCT_REVIEW_SELF_VOTE`.

**`POST /api/v1/reviews/images`** uploads one photo (`multipart/form-data`, field `file`) and
returns the `publicId`/`url` to pass back in the review body. Accepted MIME types:
`image/jpeg`, `image/png`, `image/webp`, `image/avif`.

This is a customer-scoped Cloudinary write and therefore rate limited **per account**, not per IP
(policy `media-upload`, default 10 per hour). Images whose URL is not on
`https://res.cloudinary.com/` are rejected with `400 REVIEW_IMAGE_INVALID`.

> Uploading does not attach the image. It is a two-step flow: upload, then reference the returned
> ids when creating or editing. A customer can therefore upload without submitting a review,
> leaving an orphaned asset.

### Admin â€” moderation queue

Admin only.

| Method | Route | Result |
|--------|-------|--------|
| GET | `/api/v1/admin/reviews?status=&productId=&rating=&search=&page=&pageSize=` | `200` `PagedResponse<AdminReviewResponse>` |
| GET | `/api/v1/admin/reviews/{reviewId}` | `200` `AdminReviewResponse` |
| PUT | `/api/v1/admin/reviews/{reviewId}/status` | `200` `AdminReviewResponse` |
| DELETE | `/api/v1/admin/reviews/{reviewId}` | `204` |

**`PUT â€¦/status`** â€” `{ "status": "Published" | "Rejected" | "Pending", "reason": "optional" }`.
Rejecting **requires** a reason, which is shown back to the author. An unrecognised `status`
filter or body value is `400`, never silently ignored.

Admin responses additionally include `customerId` and `customerEmail` so a moderator can identify
who submitted the review.

Errors: `PRODUCT_REVIEW_NOT_FOUND`, `PRODUCT_REVIEW_ALREADY_EXISTS`, `PRODUCT_REVIEW_SELF_VOTE`,
`REVIEW_IMAGE_INVALID`, `REVIEW_REASON_REQUIRED`, `PRODUCT_NOT_FOUND`,
`PRODUCT_VARIANT_MISMATCH`, `PRODUCT_VARIANT_UNAVAILABLE`.

---

## Authorization Summary

| Level | Endpoints |
|-------|-----------|
| Public (no auth) | Store settings, categories, brands, products, policies, OTP, published reviews |
| Customer Google auth only | `POST /auth/google` (issues application JWT) |
| Admin password auth only | `POST /auth/login`, `POST /auth/request-password-reset`, `POST /auth/reset-password` |
| Shared (any valid JWT) | `POST /auth/refresh`, `POST /auth/logout`, `POST /auth/logout-all` |
| Customer | Cart, checkout, orders, profile, addresses, wishlist, own reviews, coupon validation |
| Customer or Admin | Coupon validation |
| Public (anonymous reads) | Store settings, categories, brands, products, policies, OTP, **published product reviews** |
| Admin only | All `/admin/` routes |
| None (signed) | Payment webhook (`/api/v1/payments/webhook` â€” verified by HMAC signature) |

Frontend guards are UX only. Authorization is enforced server-side on every request.

> **Customer authentication:** Google Sign-In only. No customer password registration, login, or password reset endpoints exist.  
> **Admin authentication:** Username or email + password. Google Sign-In is not used for admin access.

---

## Tracking Configuration

Tracking is entirely optional. There are no server-side tracking calls. The backend simply exposes configured IDs through the public settings endpoint so the frontend can inject the appropriate scripts.

| Field | Source | Safe for frontend? |
|-------|--------|--------------------|
| `googleAnalyticsMeasurementId` | `Tracking__GoogleAnalyticsMeasurementId` env var | Yes â€” public browser ID |
| `metaPixelId` | `Tracking__MetaPixelId` env var | Yes â€” public browser ID |

Both fields are `null` when not configured. Set them in the store's environment variables â€” no admin API is needed to change them (requires a redeploy).

---

## Important Security Notes

- **Two auth domains.** Customer: Google Sign-In only â†’ application JWT. Admin: username/email + password â†’ application JWT. Neither domain's tokens work in the other's flows.
- **Never trust client prices.** All monetary values are calculated server-side.
- **JWT TokenVersion (`tv` claim)** â€” incremented on logout-all and password reset. Old JWTs rejected immediately via short 15-minute expiry; refresh tokens are revoked synchronously.
- **Google token is not the app token.** The Google ID token is validated server-side only; the application issues its own JWT. The Google token never reaches the `Authorization` header.
- **Google identity key.** Customer accounts are keyed by Google `sub` (subject), not email. Email changes do not create duplicate accounts.
- **No account takeover via email.** Linking a Google identity to an existing email account is safe â€” it only adds an `ExternalLogin` record; the existing account continues to work.
- **Coupon concurrency** â€” enforced via PostgreSQL xmin optimistic locking with retry. Usage limits cannot be exceeded under concurrent load.
- **Inventory concurrency** â€” enforced via PostgreSQL xmin on `InventoryItems`. Cannot oversell.
- **Webhook idempotency** â€” unique index on `(Provider, ProviderEventId)`. Duplicates safely ignored.
- **Password reset tokens** â€” SHA-256 hashed, 15-minute TTL, single-use, never logged or returned.
- **Refresh token reuse detection** â€” reusing a revoked token revokes the entire token family for that user (all devices).
- **Secrets** â€” never in API responses. Masking applied where status display is needed.
- **Tracking IDs** â€” GA4 and Meta Pixel IDs are public browser-safe values; exposed in `/store/settings`. No server-side tracking calls.
- **Transactional emails** â€” OrderCreated, PaymentSucceeded, OrderCancelled, PaymentFailed, OrderShipped each have dedicated email methods. Email failures are retried via Outbox but never corrupt the core order transaction.

---

## Production Monitoring (Sentry)

Sentry is integrated for production error monitoring. Configuration is optional â€” the application starts normally without a DSN.

| Config key | Required | Description |
|-----------|----------|-------------|
| `Sentry__Dsn` | Optional | Sentry project DSN. Omit to disable. |
| `Sentry__TracesSampleRate` | Optional | Performance tracing sample rate (0.0â€“1.0, default 0). |
| `Sentry__Release` | Optional | Release identifier (e.g. git SHA for source-map linking). |

**Security filters applied before sending to Sentry:**
- `Authorization` header â†’ `[Filtered]`
- `Cookie` header â†’ `[Filtered]`
- `X-Cart-Token` header â†’ `[Filtered]`
- Any header containing `secret`, `token`, `key`, `password`, `auth`, `credential` â†’ `[Filtered]`
- Request bodies are **never** sent (`MaxRequestBodySize = None`)

Sentry does not replace Serilog. Structured application logs remain in Serilog; Sentry provides exception alerting and error dashboards.

---


