# Frontend Technical Implementation Guide

**Backend:** `KromicCommerce` — ASP.NET Core 8, CQRS + MediatR, EF Core, PostgreSQL
**Scope:** Payload changes, new endpoints, and workflow integration from the backend refactor
**Audience:** Front-end engineer wiring the storefront and admin UI to these changes

All field lists below were read from the actual contract source files, not from memory. Where a
field is new, the contract file is named so you can verify it.

---

## 1. Payload Updates

### 1.1 `GET /api/v1/cart` — response gained one field

`src/KromicCommerce.Contracts/Cart/CartResponse.cs`

| Field | Type | Status | Notes |
| --- | --- | --- | --- |
| `items`, `subtotal`, `currency`, `totalItems`, `isEmpty` | — | **Unchanged** | |
| `couponCode` | `string \| null` | **ADDED** | Optional (defaults to `null`) |

```ts
interface CartResponse {
  items: CartItem[];
  subtotal: number;
  currency: string;
  totalItems: number;
  isEmpty: boolean;
  couponCode: string | null;   // NEW — normalised, upper-cased
}
```

**Critical:** `subtotal` **excludes** the coupon discount, and no discount amount is returned
from this endpoint. It shows the coupon *code* in effect so the cart can render a "coupon
applied" chip, but the payable total must come from the checkout summary (see §1.2).

Because the field is optional with a `null` default, existing clients that do not read it are
unaffected.

---

### 1.2 `GET /api/v1/checkout/summary` — entirely new response

`src/KromicCommerce.Contracts/Orders/CheckoutSummaryResponse.cs`

This is the authoritative quote. Nothing in it is client-computed.

```ts
type PaymentMethod = 'Razorpay' | 'CashOnDelivery';
type DiscountType  = 'Percentage' | 'FixedAmount';
type StockAvailability = 'InStock' | 'LowStock' | 'OutOfStock';

interface CheckoutSummaryResponse {
  // ── Lines ───────────────────────────────────────────────────────────
  items: Array<{
    cartItemId: string;
    productId: string;
    variantId: string | null;
    productName: string;
    productSlug: string;
    variantDescription: string | null;
    sku: string | null;
    unitPrice: number;
    quantity: number;
    lineTotal: number;
    stockAvailability: StockAvailability;
    canPurchase: boolean;
    primaryImageUrl: string | null;
  }>;

  // ── Payable breakdown ───────────────────────────────────────────────
  subtotal: number;
  discountAmount: number;
  taxAmount: number;
  taxLabel: string;              // e.g. "GST"
  isPriceInclusive: boolean;
  shippingAmount: number;
  codFee: number;
  grandTotal: number;            // ← the amount that will be charged
  currency: string;

  // ── Coupon ──────────────────────────────────────────────────────────
  appliedCouponCode: string | null;
  discountType: DiscountType | null;
  eligibleSubtotal: number;
  couponErrorCode: string | null;
  couponErrorMessage: string | null;

  // ── Shipping / COD ──────────────────────────────────────────────────
  isFreeShipping: boolean;
  freeShippingThreshold: number | null;
  remainingForFreeShipping: number;
  isCodAvailable: boolean;
  deliveryEstimate: {
    earliestDate: string;
    latestDate: string;
    displayText: string;
  } | null;

  // ── Payment methods ─────────────────────────────────────────────────
  isRazorpayConfigured: boolean;
  paymentMethods: Array<{
    method: PaymentMethod;
    isAvailable: boolean;
    unavailableReason: string | null;
  }>;

  // ── Readiness ───────────────────────────────────────────────────────
  isReadyToCheckout: boolean;
  blockingReasons: Array<{ code: string; message: string }>;
}
```

#### Total display rule

`grandTotal` is what gets charged. Display it directly.

- `isPriceInclusive: false` → `grandTotal` already includes `taxAmount`.
- `isPriceInclusive: true` → tax is inside the line prices and is **not** added again.

Either way, **do not add `taxAmount` to `grandTotal`.**

#### ⚠️ `appliedCouponCode` does not mean "applied"

This is the highest-risk field in the API. When a coupon is **rejected**, `appliedCouponCode`
still carries the submitted code, while `discountAmount` is `0` and `couponErrorCode` is set.

This is deliberate — `CheckoutHandler` reads it to detect that the customer expected a discount
and fails the checkout rather than silently charging full price. Do not "fix" it by trusting the
field.

```ts
// ✅ correct
const couponActive = summary.couponErrorCode === null && summary.appliedCouponCode !== null;

// ❌ wrong — renders "SAVE10 applied" above a zero discount
const couponActive = summary.appliedCouponCode !== null;
```

#### Blocking reason codes

| Code | Meaning |
| --- | --- |
| `CART_EMPTY` | No cart, or every line was dropped |
| `COD_NOT_AVAILABLE` | COD requested but disabled for the store |
| `RAZORPAY_NOT_CONFIGURED` | Online payments not set up |
| `PRODUCT_UNAVAILABLE:<id>` | A cart line's product was archived/removed |
| `VARIANT_UNAVAILABLE:<id>` | A selected variant is gone or inactive |
| `INSUFFICIENT_STOCK:<id>` | Not enough stock for a cart line |

The last three are prefix-plus-id. Match on the prefix, use the id to highlight the offending line.

`blockingReasons[].message` is display-ready. You may use it as-is or map `code` to your own copy.

#### Not-an-error case

A cart that cannot be checked out returns **`200` with zeroed totals and a reason** — not a 4xx.
Your cart screen can render the failure state without special-casing a missing cart.

---

### 1.3 `POST /api/v1/cart/coupon` and `DELETE /api/v1/cart/coupon`

**New request** — `src/KromicCommerce.Contracts/Cart/ApplyCouponRequest.cs`

```ts
interface ApplyCouponRequest {
  couponCode: string;   // exactly as the customer typed it; normalised server-side
}
```

No discount amount field exists, by design.

**Response:** both endpoints return the full `CheckoutSummaryResponse` (identical to §1.2).

`DELETE` is idempotent — it succeeds whether or not a coupon was applied.

---

### 1.4 `GET /api/v1/checkout/summary` — request

`GetCheckoutSummaryRequest` — query parameters, no body, **no monetary fields**.

| Param | Type | Required | Notes |
| --- | --- | --- | --- |
| `paymentMethod` | `Razorpay \| CashOnDelivery` | No | Omit while the customer is still choosing |
| `couponCode` | `string` | No | Overrides the cart's coupon **for this call only** |

A blank/whitespace `couponCode` means "no override" — it does **not** remove the coupon on the cart.

---

### 1.5 `POST /api/v1/products/{productId}/variants` — request field made optional

`src/KromicCommerce.Contracts/Catalog/CreateVariantRequest.cs`

| Field | Type | Before | After |
| --- | --- | --- | --- |
| `sku` | `string \| null` | required | unchanged |
| `priceOverride` | `number \| null` | required | unchanged |
| `sortOrder` | `int` | `= 0` | **`int?` = `null`** |
| `attributeValueIds` | `string[] \| null` | `= null` | unchanged |

**Behaviour change:** omitting `sortOrder` (or sending `null`) now **appends the variant after the
current maximum** instead of defaulting to position `0`. If your admin UI previously relied on
the `0` default to push a new variant to the top, you must now send an explicit `sortOrder`.

Omitting it is the recommended path for the common "add one variant" flow — the admin never has
to manage ordering.

---

### 1.6 Variant responses gained resolved attributes

Both `VariantResponse` (admin) and `StorefrontVariantResponse` (storefront) gained an
**optional** trailing field, so both are backward compatible.

`src/KromicCommerce.Contracts/Catalog/ProductAttributeContracts.cs`

```ts
interface VariantAttributeValueResponse {
  attributeValueId: string;
  attributeId: string;
  attributeName: string;   // e.g. "Storage"
  value: string;           // e.g. "128GB"
}
```

| Field | Status | Notes |
| --- | --- | --- |
| `attributes` | **ADDED**, optional | Resolved to display names — no second round-trip needed to build a selector |
| `attributeValueIds` | **Unchanged** | Raw CSV of IDs, retained for backward compatibility |
| `canPurchase` (storefront) | **Unchanged field, stricter meaning** | Now `false` for an **inactive** variant regardless of stock |

**Edge case:** values that no longer exist are omitted, so a variant whose value was deleted may
return fewer `attributes` entries than `attributeValueIds` implies. Do not assume equal counts —
use `attributes` as the display source and treat `attributeValueIds` as a legacy echo.

**Storefront backward compatibility:** products with no variants return `variants: []` and the
product's own `price` / `stockAvailability` / `canPurchase` describe the whole product. Your
existing single-variant rendering keeps working unchanged.

---

### 1.7 `GET /api/v1/store/products/{slug}` — variant projection enriched

`StorefrontVariantResponse` only (see §1.6). Additionally, inactive variants are excluded from
purchase and stock is rolled up across variants rather than read from a base row only.

---

### 1.8 `GET /api/v1/store/settings` — no shape change, one source-of-truth change

`PublicPaymentSettingsDto.codEnabled` and `DeliverySettingsDto.codEnabled` are **both unchanged
in shape** and now always carry the **same value** (sourced from a single place).

| Field | Status |
| --- | --- |
| `payment.codEnabled` | Unchanged field; now mirrors `delivery.codEnabled` |
| `delivery.codEnabled` | Unchanged field; this is now the source |
| `delivery.codExtraFee` | **Semantics changed** — see below |

**`codExtraFee` semantics changed.** It is the merchant's *configured* surcharge and is now
retained across disabling/re-enabling COD. It is **not** the chargeable amount. A store with COD
disabled can legitimately report a non-zero `codExtraFee`.

- In the **admin** UI: only display/edit the COD fee input when `codEnabled` is true.
- In the **storefront** UI: never use `codExtraFee` for math. Read `codFee` from the checkout
  summary, which is always `0` when COD is unavailable.

---

### 1.9 `PUT /api/v1/admin/settings/email` — validation tightened

`src/KromicCommerce.Contracts/Store/UpdateEmailSettingsRequest.cs` — shape unchanged.

| Mode | `senderName` | `senderEmail` |
| --- | --- | --- |
| `KromicManaged` | Required | **Must be omitted or blank** — a non-blank value is **rejected (400)** |
| `CustomerBrevo` | Required | Required, must be valid **and contain a domain** |

Newly rejected (previously accepted, then failed later at send time):
`store@example` (no TLD), `store@`, `@example.com`, `store@.com`, `store@example.`

In `KromicManaged` mode the address is meaningless, so a supplied value is **rejected rather
than silently discarded** — deliberately, so your UI learns to stop sending the field.

```ts
// Correct payload for KromicManaged — omit the key entirely
{ mode: 'KromicManaged', senderName: 'My Store' }
```

`PUT /api/v1/admin/integrations/email` takes the same rules plus `enabled` and `apiKey`.

---

### 1.10 `OrderStatus` — a new state is reachable

`src/KromicCommerce.Domain/Orders/OrderStatus.cs`

**No new enum member.** `Cancelled` values can now transition to `Refunded`.

Previously `Cancelled` was terminal. Because Razorpay refunds settle asynchronously, a cancelled
order can now later become `Refunded` when the provider confirms settlement.

**Required change:** if your order-status UI or notifications treat `Refunded` as only reachable
from `Delivered`, that assumption is now wrong. An order can go `Cancelled → Refunded`.

```ts
// A cancelled order can still become Refunded later — handle this path.
const isMoneyReturned =
  orderStatus === 'Refunded';
```

---

### 1.11 Cancellation error contract (behavioural change, not a shape change)

When a customer cancels an order paid online, the backend now **refunds before cancelling**.

If the payment provider rejects the refund:

| Aspect | Value |
| --- | --- |
| Status | `409` |
| `code` | `REFUND_FAILED` |
| Order status | **Unchanged** — still live |
| Inventory | Still reserved |
| Notification | **Not sent** |

```ts
// ❌ Wrong — the order is genuinely NOT cancelled
if (res.status === 200) setOrderStatus('Cancelled');

// ✅ Correct
if (res.status === 409 && res.body.code === 'REFUND_FAILED') {
  // Order is unchanged. Show the message; do not mark it cancelled.
  showError(res.body.message);
  return;   // leave order status exactly as the server last reported it
}
```

A `200` means the refund was **accepted** by the provider, not that funds have landed. Refunds
typically settle in 5–7 business days. Word your success copy accordingly.

---

## 2. New Endpoints

### 2.1 `GET /api/v1/checkout/summary`

| | |
| --- | --- |
| **Method** | `GET` |
| **Route** | `/api/v1/checkout/summary` |
| **Auth** | Required (JWT) |
| **Trigger** | Checkout page mount; re-fetch on payment-method change; re-fetch after any cart mutation |
| **Params** | `paymentMethod?`, `couponCode?` (query string) |
| **Success** | `200` `CheckoutSummaryResponse` |
| **Errors** | `400` validation, `401`, `404` |

> Supersedes any client-side total calculation. Call it **before** rendering the payable amount.

---

### 2.2 `POST /api/v1/cart/coupon`

| | |
| --- | --- |
| **Method** | `POST` |
| **Route** | `/api/v1/cart/coupon` |
| **Auth** | Required — **authenticated customers only** (anonymous carts cannot hold a coupon) |
| **Trigger** | Customer submits the coupon code / clicks "Apply" |
| **Body** | `{ couponCode: string }` |
| **Success** | `200` `CheckoutSummaryResponse` (recalculated) |
| **Errors** | `400` rejected coupon, `401` |

Rejection codes: `COUPON_NOT_FOUND`, `COUPON_INACTIVE`, `COUPON_EXHAUSTED`,
`COUPON_CUSTOMER_LIMIT`, `COUPON_FIRST_ORDER_ONLY`, `COUPON_MINIMUM_NOT_MET`,
`COUPON_NOT_APPLICABLE`.

On rejection the cart is left **unchanged** — there is no invalid code left behind to be silently
ignored later.

---

### 2.3 `DELETE /api/v1/cart/coupon`

| | |
| --- | --- |
| **Method** | `DELETE` |
| **Route** | `/api/v1/cart/coupon` |
| **Auth** | Required |
| **Trigger** | Customer clicks "Remove coupon" |
| **Params** | None |
| **Success** | `200` `CheckoutSummaryResponse` (recalculated, no discount) |
| **Errors** | `401` |

Idempotent — safe to call when no coupon is applied.

---

### 2.4 `GET /api/v1/products/{productId}/attributes`

| | |
| --- | --- |
| **Method** | `GET` |
| **Route** | `/api/v1/products/{productId}/attributes` |
| **Auth** | `AdminOnly` |
| **Trigger** | Admin variant-editor mount |
| **Success** | `200` `ProductAttributesResponse` |
| **Errors** | `401`, `403`, `404` |

```ts
interface ProductAttributesResponse {
  productId: string;
  attributes: Array<{
    id: string;
    name: string;              // e.g. "Storage"
    sortOrder: number;
    values: Array<{ id: string; value: string; sortOrder: number }>;
  }>;
}
```

A product with no attributes returns an empty `attributes` array and behaves as a
single-variant product.

---

### 2.5 `PUT /api/v1/products/{productId}/attributes`

| | |
| --- | --- |
| **Method** | `PUT` |
| **Route** | `/api/v1/products/{productId}/attributes` |
| **Auth** | `AdminOnly` |
| **Trigger** | Admin saves one attribute definition |
| **Body** | `{ name: string, values: Array<{ value: string, id?: string }> }` |
| **Success** | `200` `ProductAttributesResponse` (full refreshed set) |
| **Errors** | `400`, `401`, `403`, `404` |

⚠️ **Replaces ONE attribute and its value list — not the whole product.** The route takes no
attribute id, so the attribute is matched **by name**; an existing attribute with the same name is
updated, otherwise created.

**The value list is replaced wholesale:**
- Omit a value's `id` → a new value is created.
- Include an existing value's `id` → edited / reordered in place.
- Omit a value entirely → it is **deleted**, and any variant referencing it becomes unconfigured.

So: **read, modify, send the complete value list back.** Never send a partial list.

---

### 2.6 `DELETE /api/v1/products/{productId}/attributes/{attributeId}`

| | |
| --- | --- |
| **Method** | `DELETE` |
| **Route** | `/api/v1/products/{productId}/attributes/{attributeId}` |
| **Auth** | `AdminOnly` |
| **Trigger** | Admin deletes an attribute |
| **Success** | `204` No Content |
| **Errors** | `401`, `403` |

Deletes the attribute and all its values. **Idempotent.**

---

## 3. Workflow Integration

### 3.1 Cart → Checkout (the main journey)

```
Cart page                          Checkout page
─────────                          ─────────────
GET /cart
  → show items
  → couponCode shows applied chip
  → SUBTOTAL IS PRE-DISCOUNT ★
                    │
                    ▼
                        GET /checkout/summary          (on mount)
                        ← items, grandTotal, isReadyToCheckout,
                          paymentMethods[], blockingReasons[]
                        render grandTotal  ← never compute
                    │
        ┌───────────┴────────────┐
        │ customer picks a method│
        ▼                        ▼
  POST /cart/coupon        GET /checkout/summary?paymentMethod=CashOnDelivery
  { couponCode }           ← re-fetch: COD fee now included
        │                        │
        │◀───────────────────────┘
        ▼
  ← CheckoutSummaryResponse
  → re-render total
  → if couponErrorCode: show message,
    coupon is NOT active
        │
        ▼
  if (!isReadyToCheckout) → render blockingReasons, stop
        │
        ▼
  POST /checkout { addressId, paymentMethod, idempotencyKey }
        │
        ├─ Razorpay → open widget (providerOrderId, razorpayKeyId)
        │              → POST /payments/verify?orderId=…
        │              → 200: paid
        │
        └─ COD      → done
```

**★** `GET /cart` `subtotal` excludes the discount. The only payable number is `grandTotal` from
the summary.

**Re-fetch the summary, never adjust locally,** after: coupon apply/remove, payment-method change,
and immediately before `POST /checkout`.

---

### 3.2 Coupon state machine

```
        (no coupon)
             │
     POST /cart/coupon
             │
      ┌──────┴───────┐
   accepted        rejected (400)
      │                  │
couponCode set      cart UNCHANGED
      │                  │
      ▼                  ▼
 GET /checkout/summary  show error, keep (no coupon)
      │
  ┌───┴────┐
valid   invalid
  │        │
applied  couponErrorCode set
         appliedCouponCode STILL SET ★
         discountAmount = 0
         │
         ▼
   DELETE /cart/coupon  →  clears
```

★ The starred state is the trap: **`appliedCouponCode` is non-null while the discount is zero.**
Branch on `couponErrorCode === null`.

Because only the code is stored server-side and the discount is recalculated on every pass, a
coupon that later expires or is exhausted **stops discounting automatically** — it can never carry
a stale amount into an order. You do not need to re-validate or expire anything client-side.

---

### 3.3 Payment method selection

The two availability flags are **not** sufficient on their own — use `paymentMethods[]`, which is
the combined per-cart verdict the server will actually enforce.

```
GET /checkout/summary            → isCodAvailable, isRazorpayConfigured
                                   paymentMethods[] ← render from THIS
customer selects COD
        │
        ▼
GET /checkout/summary?paymentMethod=CashOnDelivery
        │  (re-fetch — the fee only appears once COD is selected)
        ▼
  codFee included · grandTotal updated
        │
        ▼
  if codFee === 0 and paymentMethods[COD].isAvailable === false
      → store has COD off; render unavailableReason
```

**Do not quote a COD fee before a method is chosen.** With `paymentMethod` omitted, `codFee` is
always `0` — the customer has not asked for COD, so including it would inflate the displayed
total.

---

### 3.4 Order cancellation with refund

```
Customer taps "Cancel order"
        │
        ▼
POST /api/v1/orders/{orderId}/cancel        (customer)
  or admin status update                   (admin)
        │
   ┌────┴────────────────────────────┐
   │ internally: refund FIRST,       │
   │ then cancel, then release stock │
   └────┬────────────────────────────┘
        │
   ┌────┴─────────────┐
 200               409 REFUND_FAILED
   │                  │
order = Cancelled   order UNCHANGED ★
   │                  inventory still reserved
   │                  no email sent
   ▼                  ▼
show "refund        show message as-is,
initiated"          DO NOT mark cancelled
   │
   ▼
later: webhook settles
   → order may become Refunded ★
```

★ Two state facts your UI must handle:
1. On `409 REFUND_FAILED` the order is genuinely still live.
2. `Cancelled → Refunded` is now reachable, so a cancelled order can later show as refunded.

---

### 3.5 Admin variant editor

```
Open variant editor
        │
        ├──► GET /products/{id}/attributes        (axes + values)
        ├──► GET /products/{id}/variants          (now includes resolved `attributes`)
        │
   admin adds "Storage" with [128GB, 256GB]
        │
        ▼
PUT /products/{id}/attributes
{ name: "Storage",
  values: [ {value:"128GB"}, {value:"256GB"} ] }   ← complete list
        │
        ▼
200 → refreshed attribute set; re-render
        │
   admin creates a variant
        │
        ▼
POST /products/{id}/variants
{ sku: "…", priceOverride: 1299,
  attributeValueIds: [ … ],
  sortOrder: null }        ← null = append at end
        │
        ▼
   response includes resolved `attributes` —
   build the label without another request
```

**Editing an existing value:** include its `id`. Omitting it creates a duplicate; omitting the
value entirely deletes it and leaves any referencing variant unconfigured.

---

## 4. Integration Checklist

- [ ] Delete all client-side total/discount/tax/shipping arithmetic. Display `grandTotal`.
- [ ] Never re-add `taxAmount` to `grandTotal`, regardless of `isPriceInclusive`.
- [ ] Branch coupon state on `couponErrorCode === null`, not on `appliedCouponCode`.
- [ ] Treat `GET /cart` `subtotal` as pre-discount; do not show it as the payable total.
- [ ] Render payment options from `paymentMethods[]`, using `unavailableReason`.
- [ ] Re-fetch the summary on payment-method change and after coupon mutations.
- [ ] Send `idempotencyKey` on `POST /checkout` (protects against duplicate orders on retry).
- [ ] On `409 REFUND_FAILED`, leave order state untouched and show the server message.
- [ ] Handle `Cancelled → Refunded` in order-status UI and notifications.
- [ ] Hide the COD fee input in admin when COD is disabled; never use `codExtraFee` for storefront math.
- [ ] Hide/disable the sender-email field when email mode is `KromicManaged`.
- [ ] Omit `sortOrder` when appending variants; send it explicitly only to control position.
- [ ] For `PUT /attributes`, always send the **complete** value list, including existing ids.
- [ ] Use variant `attributes` for display; do not assume it matches `attributeValueIds` in length.

---

## 5. Verification

The backend test suites are the executable specification for every behaviour above:

| File | Covers |
| --- | --- |
| `tests/…/Application/CheckoutSummaryPricingTests.cs` | Totals, tax modes, blocking reasons, COD |
| `tests/…/Application/OrderCancellationRefundTests.cs` | Refund-before-cancel ordering, rejection behaviour |
| `tests/…/Application/EmailSettingsValidationTests.cs` | Mode-conditional validation |
| `tests/…/Domain/CashOnDeliveryConfigurationTests.cs` | COD fee semantics |
| `tests/…/Application/StorefrontCacheInvalidationTests.cs` | Cache correctness |
| `tests/…/Domain/OrderCancellationStateTests.cs` | `CanCancel` per status, `Cancelled → Refunded` |
