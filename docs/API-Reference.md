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

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| POST | `/register` | None | Register a new customer |
| POST | `/login` | None | Login with email or username + password |
| POST | `/google` | None | Authenticate via Google Sign-In ID token |
| POST | `/refresh` | None | Rotate refresh token, get new access token |
| POST | `/logout` | Customer | Revoke current device refresh token |
| POST | `/logout-all` | Customer | Revoke all refresh tokens, increment TokenVersion |
| POST | `/request-password-reset` | None | Send reset token to admin email (always 204) |
| POST | `/reset-password` | None | Reset password using email + token |

**Login request:** `{ email, password, deviceHint? }` — `email` accepts email address or username.  
**Register request:** `{ email, password, firstName?, lastName?, phoneNumber? }`  
**Password reset request:** `{ email }` — never reveals whether account exists.  
**Reset password request:** `{ email, token, newPassword, confirmPassword }`  

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

## Categories

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/api/v1/store/categories` | None | All active categories with product counts |
| GET | `/api/v1/store/categories/{slug}` | None | Category by slug |
| GET | `/api/v1/categories` | Admin | All categories (admin view) |
| POST | `/api/v1/categories` | Admin | Create category |
| PUT | `/api/v1/categories/{id}` | Admin | Update category |
| DELETE | `/api/v1/categories/{id}` | Admin | Delete (fails if has children or products) |

---

## Brands

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/api/v1/store/brands` | None | All active brands with product counts |
| GET | `/api/v1/store/brands/{slug}` | None | Brand by slug |
| GET | `/api/v1/brands` | Admin | All brands (admin view) |
| POST | `/api/v1/brands` | Admin | Create brand |
| PUT | `/api/v1/brands/{id}` | Admin | Update brand |
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

**Checkout request:** `{ shippingAddress, paymentMethod, couponCode?, idempotencyKey? }`  
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
| GET | `/api/v1/admin/integrations/razorpay` | Admin | Razorpay status (masked, no secrets) |
| PUT | `/api/v1/admin/integrations/razorpay` | Admin | Update Razorpay config |
| GET | `/api/v1/admin/integrations/google` | Admin | Google OAuth status |
| PUT | `/api/v1/admin/integrations/google` | Admin | Update Google OAuth config |
| GET | `/api/v1/admin/integrations/brevo` | Admin | Brevo email status |
| PUT | `/api/v1/admin/integrations/brevo` | Admin | Update Brevo config |
| GET | `/api/v1/admin/integrations/sms` | Admin | SMS provider status |
| PUT | `/api/v1/admin/integrations/sms` | Admin | Update SMS config |

**Security:** Secrets are never returned. Responses contain `isConfigured` flag and masked identifiers only.

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
| Public (no auth) | Store settings, categories, brands, products, policies, OTP, auth |
| Customer | Cart, checkout, orders, profile, addresses, coupon validation |
| Customer or Admin | Coupon validation |
| Admin only | All `/admin/` routes |
| None (signed) | Payment webhook (`/payments/webhook` — verified by HMAC signature) |

Frontend guards are UX only. Authorization is enforced server-side on every request.

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

- **Never trust client prices.** All monetary values are calculated server-side.
- **JWT TokenVersion** — incremented on logout-all and password reset. Old JWTs rejected immediately.
- **Coupon concurrency** — enforced via PostgreSQL xmin optimistic locking with retry. Usage limits cannot be exceeded under concurrent load.
- **Inventory concurrency** — enforced via PostgreSQL xmin on `InventoryItems`. Cannot oversell.
- **Webhook idempotency** — unique index on `(Provider, ProviderEventId)`. Duplicates safely ignored.
- **Password reset tokens** — SHA-256 hashed, 15-minute TTL, single-use, never logged or returned.
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
