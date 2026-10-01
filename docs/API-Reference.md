# Kromic Commerce — Backend API Reference

All endpoints follow the pattern `/api/v1/...` and return JSON.

**Authentication:** JWT Bearer token in the `Authorization: Bearer <token>` header.  
**Pagination:** Paginated endpoints return `PagedResponse<T>` with `items`, `page`, `pageSize`, `totalCount`, `totalPages`, `hasNextPage`, `hasPreviousPage`.  
**Error envelope:**
```json
{ "success": false, "error": { "code": "ERROR_CODE", "message": "Human-readable message" } }
```

---

## Auth — `POST /api/v1/auth/...`

The system has two completely separate authentication domains. Customer and admin authentication are independent; they share token infrastructure but nothing else.

### Customer authentication — Google Sign-In only

Customers authenticate exclusively via Google Sign-In. There is no customer password registration, no customer email/password login, and no customer password reset.

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| POST | `/auth/google` | None | Customer Google Sign-In |
| POST | `/auth/refresh` | None | Rotate refresh token, get new access token |
| POST | `/auth/logout` | Bearer | Revoke current device refresh token |
| POST | `/auth/logout-all` | Bearer | Revoke all refresh tokens, increment TokenVersion |

**Google Sign-In flow:**

```
Customer clicks "Continue with Google"
       ↓
Google Sign-In completes in the browser/app
       ↓
Client receives a Google ID token (credential)
       ↓
POST /auth/google  { idToken, deviceHint? }
       ↓
Backend validates ID token against Google's public keys
       ↓
Backend finds or creates customer account (keyed by Google subject, not email)
       ↓
Backend issues application JWT + refresh token
       ↓
200 { accessToken, refreshToken, accessTokenExpiresInSeconds, tokenType }
```

**Google request:** `{ idToken, deviceHint? }`  
**The Google ID token is validated server-side — it is never used as the application authorization credential.**

**Account resolution on Google callback:**

| Scenario | Outcome |
|----------|---------|
| Google subject already linked | Returns existing customer |
| Google subject not found, email matches existing account | Links Google identity to existing account |
| Google subject not found, no email match | Creates new customer + CustomerProfile |
| Account deactivated | 403 `AUTH_ACCOUNT_INACTIVE` |
| Invalid / expired Google token | 401 `AUTH_GOOGLE_INVALID` |

**Customer identity key:** Google `sub` (subject) claim — stable across email changes. Email is stored as reference only and is never used as the primary identity key.

---

### Admin authentication — username or email + password

Admins authenticate with a username or email address plus a password. Google Sign-In is not used for admin authentication.

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| POST | `/auth/login` | None | Admin login (username or email + password) |
| POST | `/auth/refresh` | None | Rotate refresh token, get new access token |
| POST | `/auth/logout` | Bearer | Revoke current device refresh token |
| POST | `/auth/logout-all` | Bearer | Revoke all refresh tokens, increment TokenVersion |
| POST | `/auth/request-password-reset` | None | Send reset token to admin email (always 204) |
| POST | `/auth/reset-password` | None | Complete reset using email + token |

**Login request:** `{ identifier, password, deviceHint? }` — `identifier` accepts either an email address or a username.

**Admin login flow:**

```
POST /auth/login  { identifier: "admin@example.com" | "kromic_admin", password }
       ↓
Lookup by NormalizedEmail OR NormalizedUsername (case-insensitive)
       ↓
Verify password (PBKDF2/HMAC-SHA256)
       ↓
200 { accessToken, refreshToken, accessTokenExpiresInSeconds, tokenType }
```

**Admin password reset flow:**

```
POST /auth/request-password-reset  { email }
       → Always 204 (never reveals whether the email exists)
       → If admin found: generates cryptographically random token,
         stores SHA-256 hash, sends raw token via Brevo email
       → Token TTL: 15 minutes, single-use

POST /auth/reset-password  { email, token, newPassword, confirmPassword }
       → Validates token hash, checks expiry
       → On success: updates password hash, increments TokenVersion,
         revokes all refresh tokens (all devices logged out)
       → Token is cleared after first use
```

**Password reset request:** `{ email }` — never reveals whether the account exists (enumeration-safe).  
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
| Refresh token storage | SHA-256 hash only — raw token returned once, never stored |
| Rotation | Every refresh issues a new token and revokes the old one |
| Reuse detection | Reusing a revoked token revokes the entire token family |
| Token versioning | `tv` claim embedded in JWT; incremented on logout-all and password reset |

**JWT claims:** `sub` (userId), `email`, `role` (Customer/Admin), `tv` (tokenVersion), `jti` (unique token ID).

**Refresh token rotation:**

```
POST /auth/refresh  { refreshToken, deviceHint? }
       ↓
Hash incoming token → lookup by hash
       ↓
If already revoked → revoke all tokens for this user → 401 (reuse detected)
If expired → 401
       ↓
Issue new refresh token, revoke old one (linked for audit chain)
       ↓
200 { accessToken, refreshToken, ... }
```

**Logout:**

```
POST /auth/logout  { refreshToken }         — revokes single device token
POST /auth/logout-all                       — revokes all tokens + increments TokenVersion
```  

---

## Admin Bootstrap — `POST /api/v1/admin/bootstrap`

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| POST | `/admin/bootstrap` | None (secret required) | Create first admin (one-time only) |

**Request:** `{ email, password, firstName, lastName, businessName, bootstrapSecret, username? }`  
Rejected with 409 if any admin already exists. Rate-limited: 3 requests per 15 minutes per IP.

---

## Store Settings — Admin

All require `AdminOnly`.

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/v1/admin/settings` | Full business settings |
| PUT | `/api/v1/admin/settings/basic` | Name, contact, social links |
| PUT | `/api/v1/admin/settings/locale` | Country, currency, timezone, culture |
| PUT | `/api/v1/admin/settings/delivery` | Shipping fees, COD, delivery days |
| PUT | `/api/v1/admin/settings/auth` | Auth methods, OTP config |
| PUT | `/api/v1/admin/settings/email` | Email mode and sender identity |
| PUT | `/api/v1/admin/settings/seo` | Meta title, description, favicon |
| PUT | `/api/v1/admin/settings/status` | Open/closed state |
| GET | `/api/v1/admin/tax` | Current tax configuration |
| PUT | `/api/v1/admin/tax` | Update tax config (enabled, %, inclusive, label) |

---

## Store Settings — Public

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/api/v1/store/settings` | None | Public store info (no secrets) |

Returns: store name, locale, hours, delivery config, SEO meta, social links (WhatsApp, LinkedIn, Instagram, Facebook, Twitter, YouTube), and safe tracking IDs for client-side analytics — never email keys, JWT secrets, or provider credentials.

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

## Carousel — Home page

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
| `title` | string | Yes | 1–200 characters |
| `subtitle` | string \| null | No | ≤ 500 characters |
| `ctaText` | string \| null | No | ≤ 50 characters. A label such as "Shop Now", not a link |
| `sortOrder` | int | No (default `0`) | ≥ 0. Lower sorts first |
| `isActive` | bool | No (default `false`) | Whether the storefront shows the slide |

`PUT /api/v1/admin/carousel/{id}` takes the same fields, all required on update.

### Public response

`GET /api/v1/store/carousel` returns an array (empty when nothing is configured — never a 404):

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
no URL field anywhere in the request or response models — a slide cannot be pointed at an arbitrary
site. The storefront should navigate to `ctaTarget`.

Only active slides that have an image are returned. Ordering is `sortOrder` ascending, then creation
time, then id, so slides sharing a display order always come back in the same sequence.

---

## Categories

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/api/v1/store/categories` | None | All active categories with product counts |
| GET | `/api/v1/store/categories/{slug}` | None | Category by slug |
| GET | `/api/v1/categories` | Admin | All categories (admin view) |
| POST | `/api/v1/categories` | Admin | Create category |
| PUT | `/api/v1/categories/{id}` | Admin | Update category (`isActive` is optional; omit it to preserve the current state) |
| PUT | `/api/v1/categories/{id}/image` | Admin | Upload or replace a category image |
| DELETE | `/api/v1/categories/{id}/image` | Admin | Remove a category image |
| DELETE | `/api/v1/categories/{id}` | Admin | Delete (fails if has children or products) |

---

## Brands

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/api/v1/store/brands` | None | All active brands with product counts |
| GET | `/api/v1/store/brands/{slug}` | None | Brand by slug |
| GET | `/api/v1/brands` | Admin | All brands (admin view) |
| POST | `/api/v1/brands` | Admin | Create brand |
| PUT | `/api/v1/brands/{id}` | Admin | Update brand (`isActive` is optional; omit it to preserve the current state) |
| DELETE | `/api/v1/brands/{id}` | Admin | Delete (fails if has products) |

---

## Products

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/api/v1/store/products` | None | Paginated storefront catalog |
| GET | `/api/v1/store/products/{slug}` | None | Product detail by slug |
| GET | `/api/v1/store/products/{slug}/related` | None | Related products |
| GET | `/api/v1/store/featured` | None | Featured products |
| GET | `/api/v1/products` | Admin | Paginated admin catalog |
| GET | `/api/v1/products/{id}` | Admin | Admin product detail |
| POST | `/api/v1/products` | Admin | Create product |
| PUT | `/api/v1/products/{id}` | Admin | Update product |
| POST | `/api/v1/products/{id}/publish` | Admin | Publish product |
| POST | `/api/v1/products/{id}/archive` | Admin | Archive product |
| DELETE | `/api/v1/products/{id}` | Admin | Delete product |

**Storefront query parameters:** `search`, `categoryId`, `brandId`, `inStockOnly`, `minPrice`, `maxPrice`, `sortBy` (`price_asc`/`price_desc`/`name`/`newest`), `page`, `pageSize`.  
**Important:** Prices come from server. `getEffectivePrice(variant)` is the single source of truth. Never trust client-provided prices.

---

## Product Images

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| POST | `/api/v1/products/{id}/images` | Admin | Upload image to Cloudinary |
| DELETE | `/api/v1/products/{id}/images/{imageId}` | Admin | Delete image |
| PUT | `/api/v1/products/{id}/images/{imageId}/primary` | Admin | Set primary image |
| PUT | `/api/v1/products/{id}/images/reorder` | Admin | Reorder images |

---

## Inventory

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/api/v1/inventory/{productId}` | Admin | Get inventory for product |
| PUT | `/api/v1/inventory/{productId}` | Admin | Set on-hand quantity |
| POST | `/api/v1/inventory/{productId}/adjust` | Admin | Adjust by delta (+/-) |

Stock is concurrency-safe via PostgreSQL xmin. Cannot go negative. Reserved stock cannot be removed until released.

---

## Cart

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/api/v1/cart` | None | Get current cart |
| POST | `/api/v1/cart/items` | None | Add item to cart |
| PUT | `/api/v1/cart/items/{id}` | None | Update item quantity |
| DELETE | `/api/v1/cart/items/{id}` | None | Remove item |
| DELETE | `/api/v1/cart` | None | Clear cart |

**Anonymous carts:** Pass `X-Cart-Token` header (max 64 chars) with the server-issued token. Token is returned in the first `AddCartItem` response for anonymous carts.  
**Authenticated carts:** Cart is resolved from JWT `sub` claim. No token needed.  
**Important:** Cart stores product IDs only — no prices. Checkout always recalculates from the database.

---

## Checkout

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| POST | `/api/v1/checkout` | Customer | Initiate checkout |
| POST | `/api/v1/payments/verify` | Customer | Verify Razorpay payment after widget |
| POST | `/api/v1/payments/webhook` | None (signed) | Razorpay webhook (idempotent) |

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

## Orders — Customer

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/api/v1/orders` | Customer | Paginated order history |
| GET | `/api/v1/orders/{id}` | Customer | Order detail (customer-scoped) |
| POST | `/api/v1/orders/{id}/cancel` | Customer | Cancel order (if cancellable) |

Customer can only access their own orders. `CustomerId` comes from JWT, never from request body.

---

## Orders — Admin

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/api/v1/admin/orders` | Admin | Paginated orders with filters |
| GET | `/api/v1/admin/orders/{id}` | Admin | Any order detail |
| PUT | `/api/v1/admin/orders/{id}/status` | Admin | Advance order status |
| POST | `/api/v1/admin/orders/{id}/cancel` | Admin | Cancel any order |

**Admin order filters:** `search`, `status`, `fromDate`, `toDate`, `sortBy`, `sortDirection`, `page`, `pageSize`.

---

## Promotions — Admin

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

## Promotions — Customer

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| POST | `/api/v1/store/promotions/validate` | Customer | Validate coupon against current cart |

**Request:** `{ couponCode }` — server recalculates cart from database, never trusts client subtotal.  
**Note:** A valid response here does NOT guarantee the coupon remains valid at checkout. Checkout re-validates.

---

## Customer Profile

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/api/v1/me` | Customer | Current user info |
| GET | `/api/v1/me/profile` | Customer | Customer profile |
| PUT | `/api/v1/me/profile` | Customer | Update profile |
| POST | `/api/v1/me/profile/avatar` | Customer | Upload avatar |

---

## Customer Addresses

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/api/v1/me/addresses` | Customer | All addresses |
| GET | `/api/v1/me/addresses/{id}` | Customer | Address by ID |
| POST | `/api/v1/me/addresses` | Customer | Create address |
| PUT | `/api/v1/me/addresses/{id}` | Customer | Update address |
| DELETE | `/api/v1/me/addresses/{id}` | Customer | Delete address |
| POST | `/api/v1/me/addresses/{id}/set-default` | Customer | Set as default |

Addresses are customer-scoped. A customer cannot access another customer's addresses.

---

## Integration Config — Admin

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/api/v1/admin/integrations/payment` | Admin | Razorpay status (masked, no secrets) |
| PUT | `/api/v1/admin/integrations/payment` | Admin | Update Razorpay config |
| GET | `/api/v1/admin/integrations/google` | Admin | Google OAuth status |
| PUT | `/api/v1/admin/integrations/google` | Admin | Update Google OAuth config |
| GET | `/api/v1/admin/integrations/email` | Admin | Brevo email status |
| PUT | `/api/v1/admin/integrations/email` | Admin | Update Brevo config |
| GET | `/api/v1/admin/integrations/sms` | Admin | SMS provider status |
| PUT | `/api/v1/admin/integrations/sms` | Admin | Update SMS config |

**Security:** Secrets are never returned. Responses contain `isConfigured` flag and masked identifiers only.

**Cash on delivery:** COD is **not** configured here. It is a shipping concern and has exactly one
configuration path: `PUT /api/v1/admin/settings/delivery`, which sets `codEnabled` and
`codExtraFee` together. The former `PUT /api/v1/admin/integrations/payment/cod` endpoint has been
**removed** — it created a second surface for one switch, which is what let the Integrations and
Shipping screens disagree about availability. New stores have COD disabled by default.

**Tracking IDs** (GA4, Meta Pixel) are configured via environment variables (`Tracking__GoogleAnalyticsMeasurementId`, `Tracking__MetaPixelId`) — no admin API endpoint needed. They appear in the public `/store/settings` response.

---

## Payments — Webhook

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| POST | `/api/v1/payments/webhook` | None (HMAC signed) | Razorpay webhook handler |

Razorpay sends a signed webhook body. The handler verifies the HMAC-SHA256 signature against `Razorpay__WebhookSecret` before any processing.  
**Idempotent** — duplicate webhooks with the same `ProviderEventId` are silently ignored (unique index on `WebhookEvents`).  
**Atomic** — payment state + `WebhookEvent` insert happen in a single transaction; a concurrent duplicate hitting the unique constraint returns 200 safely.  
**Never trust** payment status from the webhook body alone — it is only acted on after signature verification.

---

## OTP

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| POST | `/api/v1/otp/send` | None | Send OTP to phone number |
| POST | `/api/v1/otp/verify` | None | Verify OTP and authenticate |

Rate-limited: OTP policy (configurable, default 5/60s).

---

## Health

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/health` | Liveness check (always fast) |
| GET | `/health/ready` | Readiness check (requires PostgreSQL) |

Health responses never expose connection strings, credentials, or stack traces.

---

## Authorization Summary

| Level | Endpoints |
|-------|-----------|
| Public (no auth) | Store settings, categories, brands, products, policies, OTP |
| Customer Google auth only | `POST /auth/google` (issues application JWT) |
| Admin password auth only | `POST /auth/login`, `POST /auth/request-password-reset`, `POST /auth/reset-password` |
| Shared (any valid JWT) | `POST /auth/refresh`, `POST /auth/logout`, `POST /auth/logout-all` |
| Customer | Cart, checkout, orders, profile, addresses, coupon validation |
| Customer or Admin | Coupon validation |
| Admin only | All `/admin/` routes |
| None (signed) | Payment webhook (`/payments/webhook` — verified by HMAC signature) |

Frontend guards are UX only. Authorization is enforced server-side on every request.

> **Customer authentication:** Google Sign-In only. No customer password registration, login, or password reset endpoints exist.  
> **Admin authentication:** Username or email + password. Google Sign-In is not used for admin access.

---

## Tracking Configuration

Tracking is entirely optional. There are no server-side tracking calls. The backend simply exposes configured IDs through the public settings endpoint so the frontend can inject the appropriate scripts.

| Field | Source | Safe for frontend? |
|-------|--------|--------------------|
| `googleAnalyticsMeasurementId` | `Tracking__GoogleAnalyticsMeasurementId` env var | Yes — public browser ID |
| `metaPixelId` | `Tracking__MetaPixelId` env var | Yes — public browser ID |

Both fields are `null` when not configured. Set them in the store's environment variables — no admin API is needed to change them (requires a redeploy).

---

## Important Security Notes

- **Two auth domains.** Customer: Google Sign-In only → application JWT. Admin: username/email + password → application JWT. Neither domain's tokens work in the other's flows.
- **Never trust client prices.** All monetary values are calculated server-side.
- **JWT TokenVersion (`tv` claim)** — incremented on logout-all and password reset. Old JWTs rejected immediately via short 15-minute expiry; refresh tokens are revoked synchronously.
- **Google token is not the app token.** The Google ID token is validated server-side only; the application issues its own JWT. The Google token never reaches the `Authorization` header.
- **Google identity key.** Customer accounts are keyed by Google `sub` (subject), not email. Email changes do not create duplicate accounts.
- **No account takeover via email.** Linking a Google identity to an existing email account is safe — it only adds an `ExternalLogin` record; the existing account continues to work.
- **Coupon concurrency** — enforced via PostgreSQL xmin optimistic locking with retry. Usage limits cannot be exceeded under concurrent load.
- **Inventory concurrency** — enforced via PostgreSQL xmin on `InventoryItems`. Cannot oversell.
- **Webhook idempotency** — unique index on `(Provider, ProviderEventId)`. Duplicates safely ignored.
- **Password reset tokens** — SHA-256 hashed, 15-minute TTL, single-use, never logged or returned.
- **Refresh token reuse detection** — reusing a revoked token revokes the entire token family for that user (all devices).
- **Secrets** — never in API responses. Masking applied where status display is needed.
- **Tracking IDs** — GA4 and Meta Pixel IDs are public browser-safe values; exposed in `/store/settings`. No server-side tracking calls.
- **Transactional emails** — OrderCreated, PaymentSucceeded, OrderCancelled, PaymentFailed, OrderShipped each have dedicated email methods. Email failures are retried via Outbox but never corrupt the core order transaction.

---

## Production Monitoring (Sentry)

Sentry is integrated for production error monitoring. Configuration is optional — the application starts normally without a DSN.

| Config key | Required | Description |
|-----------|----------|-------------|
| `Sentry__Dsn` | Optional | Sentry project DSN. Omit to disable. |
| `Sentry__TracesSampleRate` | Optional | Performance tracing sample rate (0.0–1.0, default 0). |
| `Sentry__Release` | Optional | Release identifier (e.g. git SHA for source-map linking). |

**Security filters applied before sending to Sentry:**
- `Authorization` header → `[Filtered]`
- `Cookie` header → `[Filtered]`
- `X-Cart-Token` header → `[Filtered]`
- Any header containing `secret`, `token`, `key`, `password`, `auth`, `credential` → `[Filtered]`
- Request bodies are **never** sent (`MaxRequestBodySize = None`)

Sentry does not replace Serilog. Structured application logs remain in Serilog; Sentry provides exception alerting and error dashboards.

---

## Product Variants — Admin (Phase 11)

All require `AdminOnly`. Variants are nested under their parent product.

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/v1/products/{productId}/variants` | List all variants with current stock levels |
| GET | `/api/v1/products/{productId}/variants/{variantId}` | Get single variant with stock |
| POST | `/api/v1/products/{productId}/variants` | Create variant (auto-creates InventoryItem at 0 stock) |
| PUT | `/api/v1/products/{productId}/variants/{variantId}` | Update variant fields and active state |
| DELETE | `/api/v1/products/{productId}/variants/{variantId}` | Delete variant + its inventory record |

**VariantResponse:** `id`, `sku` (nullable, globally unique when set), `priceOverride` (null = inherits product base price), `sortOrder`, `isActive`, `attributeValueIds` (comma-separated sorted Guid string), `availableStock`.

**Create/Update request:** `sku?`, `priceOverride?`, `sortOrder`, `isActive` (update only), `attributeValueIds?` (list of Guid).

**Server-enforced validation rules:**
- SKU globally unique across all variants (when provided)
- Every `attributeValueId` must belong to an attribute of this product
- Max one value per attribute per variant (no two colors on same variant)
- Attribute-value combination is order-insensitive: `[ColorBlack, SizeLarge]` ≡ `[SizeLarge, ColorBlack]`
- Duplicate combination rejected with `VARIANT_DUPLICATE_COMBINATION`
- `priceOverride` ≥ 0; `sortOrder` ≥ 0

**Inventory:** Creating a variant auto-creates an `InventoryItem` at `onHand = 0`. Use `PUT /api/v1/inventory/{productId}` to set real stock. Deleting a variant also removes its inventory record. Existing order snapshots are unaffected (order items store immutable price/name copies).
