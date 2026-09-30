# Frontend Integration Guide

Backend: `KromicCommerce` (ASP.NET Core 8, CQRS + MediatR, EF Core, PostgreSQL)

This guide covers the endpoints and behaviours introduced or changed in the recent backend
refactor. It is written for the frontend team integrating against the API.

---

## 0. The one rule that matters most

**Never compute a monetary total on the client.**

The backend is the single source of truth for pricing, availability, coupons, shipping,
cash-on-delivery, tax, and order state. Every amount in the checkout flow is server-calculated
from live catalog data at the moment the request is served.

What this means concretely:

| Do not | Do instead |
| --- | --- |
| Sum cart line prices in JS | Read `Subtotal` from the summary |
| Apply a discount percentage client-side | Read `DiscountAmount` from the summary |
| Add a shipping fee to reach a total | Read `ShippingAmount` |
| Add a COD surcharge | Read `CodFee` |
| Compute tax | Read `TaxAmount`; note `IsPriceInclusive` |
| Decide whether checkout is possible | Read `IsReadyToCheckout` and `BlockingReasons` |
| Decide whether COD is offered | Read `IsCodAvailable` |
| Decide whether an item is purchasable | Read `CanPurchase` on each line |

The reason is not defensive style. A total computed on the client can disagree with the order
the server creates, which produces either an unexplained discrepancy or an order that cannot be
fulfilled. The backend deliberately accepts **no monetary values** on any request DTO — check
`GetCheckoutSummaryRequest` and `CheckoutRequest`: there is no amount field to send.

---

## 1. Checkout summary

### `GET /api/v1/checkout/summary`

The authoritative pre-payment quote. Call this before rendering the order total, and again
whenever anything that could change the total happens (cart edit, payment method change, coupon
change).

**Authentication required.**

#### Query parameters

| Parameter | Type | Notes |
| --- | --- | --- |
| `paymentMethod` | `Razorpay` \| `CashOnDelivery` | Optional. Omit while the customer is still choosing. |
| `couponCode` | string | Optional. Overrides the coupon stored on the cart for this call only. |

A blank or whitespace `couponCode` means "no override" — it does **not** remove the coupon
already on the cart.

#### Response

```jsonc
{
  "items": [
    {
      "cartItemId": "…", "productId": "…", "variantId": "…" | null,
      "productName": "…", "productSlug": "…",
      "variantDescription": "…", "sku": "…",
      "unitPrice": 1200.00, "quantity": 2, "lineTotal": 2400.00,
      "stockAvailability": "InStock",       // InStock | LowStock | OutOfStock
      "canPurchase": true,
      "primaryImageUrl": "…"
    }
  ],

  "subtotal": 2400.00,
  "discountAmount": 240.00,
  "taxAmount": 403.20,
  "taxLabel": "GST",
  "isPriceInclusive": false,
  "shippingAmount": 50.00,
  "codFee": 0.00,
  "grandTotal": 2613.20,
  "currency": "INR",

  "appliedCouponCode": "SAVE10",
  "discountType": "Percentage",
  "eligibleSubtotal": 2400.00,
  "couponErrorCode": null,
  "couponErrorMessage": null,

  "isFreeShipping": false,
  "freeShippingThreshold": 5000.00,
  "remainingForFreeShipping": 2600.00,
  "isCodAvailable": true,
  "deliveryEstimate": { "earliestDate": "…", "latestDate": "…", "displayText": "3–7 business days" },

  "isRazorpayConfigured": true,
  "paymentMethods": [
    { "method": "Razorpay",       "isAvailable": true,  "unavailableReason": null },
    { "method": "CashOnDelivery", "isAvailable": false, "unavailableReason": "Cash on delivery is not available for this store." }
  ],

  "isReadyToCheckout": true,
  "blockingReasons": []
}
```

#### How to render the total

`grandTotal` is the amount that will be charged. Use it directly.

- **Exclusive tax** (`isPriceInclusive: false`) — `grandTotal` already includes `taxAmount`.
- **Inclusive tax** (`isPriceInclusive: true`) — tax is inside the line prices, and is *not*
  added again. `grandTotal` still equals what is charged.

Either way, **display `grandTotal`**. Never re-add `taxAmount` to it.

#### Readiness and blocking reasons

`isReadyToCheckout` is the single authoritative answer. When it is `false`, `blockingReasons`
carries `{ code, message }` pairs. `message` is display-ready; you may use it as-is or map
`code` to your own copy.

| Code | Meaning |
| --- | --- |
| `CART_EMPTY` | No cart, or every line was dropped. |
| `COD_NOT_AVAILABLE` | COD was requested but the store has it switched off. |
| `RAZORPAY_NOT_CONFIGURED` | Online payments are not set up. |
| `PRODUCT_UNAVAILABLE:<id>` | A cart line's product was archived or removed. |
| `VARIANT_UNAVAILABLE:<id>` | A selected variant is gone or inactive. |
| `INSUFFICIENT_STOCK:<id>` | Not enough stock for a cart line. |

Note the prefix-and-id form. Compare on the prefix, and use the id to identify the offending
line.

A summary is **not** an error response here. A cart that cannot be checked out still returns
`200` with zeroed totals and a reason, so the cart screen can render the failure state without
special-casing a missing cart.

#### Payment methods

Render the two methods from `paymentMethods[]`, using each entry's `isAvailable` and
`unavailableReason`. Do not infer availability from `isCodAvailable` /
`isRazorpayConfigured` alone — `paymentMethods[]` is the combined, per-cart verdict and is what
the server will enforce.

When `paymentMethod` is omitted, **no COD fee is included**, because the customer has not asked
for COD yet. Re-call the summary with `paymentMethod=CashOnDelivery` once they select it, to get
an accurate total.

---

## 2. Coupons

### `POST /api/v1/cart/coupon` — apply

```jsonc
// request
{ "couponCode": "SAVE10" }
```

Returns the **complete recalculated checkout summary** — the same shape as
`GET /checkout/summary`. Apply it to your state directly; do not recompute anything.

On success the code is stored on the cart. On rejection you get a validation error and the cart
is left unchanged, so there is no invalid code lingering to be silently ignored later.

### `DELETE /api/v1/cart/coupon` — remove

Idempotent. Succeeds whether or not a coupon was applied, and returns the recalculated summary.

### ⚠️ `appliedCouponCode` does not mean "applied"

This is the single easiest mistake to make with this API.

When a coupon is **rejected**, `appliedCouponCode` still echoes the code that was *requested*,
while `discountAmount` is `0` and `couponErrorCode` is populated. This is deliberate — the
checkout command uses that field to detect that a customer expected a discount, and fails the
checkout rather than silently charging full price.

**To decide whether a coupon is active, test `couponErrorCode === null`.**

```ts
// ✅ correct
const couponActive = summary.couponErrorCode === null && summary.appliedCouponCode !== null;
if (summary.couponErrorCode) {
  showCouponError(summary.couponErrorMessage);
}

// ❌ wrong — renders "SAVE10 applied" with a zero discount
const couponActive = summary.appliedCouponCode !== null;
```

### Why the discount is never stored

Only the coupon **code** is persisted on the cart. The discount is recalculated on every pricing
pass. A coupon that later expires, exhausts its usage limit, or stops matching the cart simply
stops discounting — it can never carry a stale amount forward into an order.

### Failed checkout with a coupon

If a customer reaches checkout and the coupon has become invalid, `POST /checkout` fails with
`couponErrorCode` rather than quietly charging full price. Surface the error and offer to remove
the coupon.

---

## 3. Placing the order

### `POST /api/v1/checkout`

```jsonc
// request — no monetary values, by design
{
  "addressId": "…",
  "paymentMethod": "Razorpay",
  "couponCode": "SAVE10",     // optional
  "idempotencyKey": "…"       // recommended
}
```

**Always send an `idempotencyKey`.** A retried request with the same key will not create a second
order. This matters on flaky mobile networks, where a timeout does not mean the request failed.

**Razorpay flow** — the response includes `providerOrderId` and `razorpayKeyId`. Open the Razorpay
widget with these, then call:

### `POST /api/v1/payments/verify?orderId={orderId}`

```jsonc
{ "razorpayPaymentId": "…", "razorpayOrderId": "…", "razorpaySignature": "…" }
```

The signature is verified server-side; a client cannot fake a successful payment. On success the
order is confirmed and inventory finalised. On failure the order and payment are marked failed
and inventory is released.

**Cash on delivery** — no widget. The order is created directly.

If you asked for a payment method that is unavailable, the request fails rather than
substituting a different one.

---

## 4. Cash on delivery

COD is configured under **shipping**, and its state lives in one place only.

Read availability from `isCodAvailable` on the checkout summary, or from the public payment
settings projection. Do not maintain your own copy or infer it.

### A behaviour worth knowing

When COD is disabled, `codFee` is always `0` — even if you request `paymentMethod=CashOnDelivery`
and even if a fee is still stored in the store's configuration. The backend will not quote a
surcharge it will not honour, and the request is reported as blocked via `COD_NOT_AVAILABLE`.

The stored `codExtraFee` in admin settings is the merchant's **configuration**, retained across
disabling and re-enabling so the surcharge is not lost. It is not the chargeable amount. **In the
admin UI, display the COD fee input only when COD is enabled** — otherwise you will show a fee
that is never charged.

---

## 5. Variants and attributes

Products may define attributes (Colour, Size, …) and variants that combine attribute values.

### Admin — product attributes

| Method | Route | Purpose |
| --- | --- | --- |
| `GET` | `/api/v1/products/{productId}/attributes` | List attributes and their values |
| `PUT` | `/api/v1/products/{productId}/attributes` | Replace the full attribute set |
| `DELETE` | `/api/v1/products/{productId}/attributes/{attributeId}` | Remove one attribute |

Attribute names must be unique within a product, and values unique within an attribute. The
database enforces both; a duplicate is a constraint violation, not a silent overwrite.

The `PUT` is a **full replace**. Read the current set, modify it, and send it back in complete.

### Admin — variants

| Method | Route |
| --- | --- |
| `GET` | `/api/v1/products/{productId}/variants` |
| `GET` | `/api/v1/products/{productId}/variants/{variantId}` |
| `POST` | `/api/v1/products/{productId}/variants` |
| `PUT` | `/api/v1/products/{productId}/variants/{variantId}` |
| `DELETE` | `/api/v1/products/{productId}/variants/{variantId}` |

Variant responses now include resolved `attributes`, so the admin UI does not need a second
round-trip to build a display label.

`sortOrder` is optional on create. Omit it and the new variant is appended after the current
maximum, which is what you usually want when adding one variant at a time. Send an explicit
value only when positioning deliberately.

### Storefront

`GET /api/v1/store/products/{slug}` now includes resolved variant attributes on each variant.
Inactive variants are excluded from purchase, and a cart line pointing at one is blocked at
checkout with `VARIANT_UNAVAILABLE`.

Variant-aware stock rollups are included, so a product with variants reports stock across them
rather than only for a base row.

---

## 6. Email settings (admin)

Email has two modes with genuinely different requirements.

| Mode | Sender name | Sender address |
| --- | --- | --- |
| `KromicManaged` | Required | **Must be omitted** |
| `CustomerBrevo` | Required | Required, must be a valid address with a domain |

### `PUT /api/v1/admin/settings/email`
```jsonc
{ "mode": "KromicManaged", "senderName": "My Store" }              // senderEmail omitted
{ "mode": "CustomerBrevo", "senderName": "My Store", "senderEmail": "store@example.com" }
```

In `KromicManaged` mode, emails are sent from Kromic's shared account, so the store has no
sender address to configure. Sending one is **rejected** rather than silently discarded — so
that an admin UI stops sending the field instead of appearing to succeed and dropping the value.

**Hide and disable the sender-address input when the mode is `KromicManaged`.** This is the
intended behaviour, not an error you need to work around.

In `CustomerBrevo` mode the address must be verified in the merchant's Brevo account. The
backend rejects malformed addresses (including ones missing a domain, such as
`store@example`) up front so the failure surfaces as a validation error rather than a delivery
error later.

### `PUT /api/v1/admin/integrations/email`

The raw integration variant additionally takes `enabled` and `apiKey`. The same mode-conditional
sender-email rules apply. The API key is encrypted at rest and is never returned by any endpoint.

---

## 7. Storefront caching behaviour you may observe

The backend uses an in-process cache. Two things are worth knowing from the client side:

- **Availability can lag very briefly.** Stock and product mutations invalidate dependent cache
  entries, but a storefront page served a moment before a stock change may briefly show the old
  value. Checkout re-pricing is authoritative, so a customer can always proceed to checkout and
  be given the current truth there.
- **Shipping estimates are scoped to a configuration epoch.** Changing delivery or COD settings
  makes every previously cached delivery estimate unreachable at once, so a customer never sees a
  stale delivery window after an admin changes it.

Neither affects correctness of checkout.

---

## 8. Error handling

Errors use a consistent envelope with a machine-readable `code` and a human-readable `message`.

| Status | Use for |
| --- | --- |
| `400` | Validation failure, including invalid coupons and malformed input |
| `401` | Missing or invalid token |
| `404` | Not found, **or** a resource belonging to another customer |
| `409` | State conflict — e.g. `REFUND_FAILED`, invalid order transition |

A `404` (not `403`) is deliberately returned for another customer's resource, so the API does not
confirm that someone else's record exists.

### Cancellation and refunds — important

When a customer cancels an order that was paid online, the backend refunds **before** cancelling.
If the payment provider rejects the refund, you get `409` with code `REFUND_FAILED` and:

- the order is **unchanged** — still live, inventory still reserved;
- no cancellation notification is sent;
- the error message states that the order is unchanged.

This is deliberate. The alternative — cancelling and refunding afterwards — would tell a customer
their order is cancelled while their money is still captured. Present the message as-is and let
the customer retry or contact support. **Do not** optimistically flip the UI to "cancelled" on
this response; the order really is still live.

Refunds settle asynchronously with the provider (typically 5–7 business days). A successful
cancellation means the refund was **accepted**, not that funds have landed.

---

## 9. End-to-end example

```
1. GET  /api/v1/cart
2. GET  /api/v1/checkout/summary                       → render grandTotal
3. POST /api/v1/cart/coupon  { couponCode: "SAVE10" }  → re-render returned summary
4. GET  /api/v1/checkout/summary?paymentMethod=CashOnDelivery
                                                      → re-render (COD fee now included)
5. if (!isReadyToCheckout) show blockingReasons
6. POST /api/v1/checkout { addressId, paymentMethod, idempotencyKey }
7. Razorpay: open widget, then POST /api/v1/payments/verify?orderId=…
```

Re-fetch the summary at steps 3, 4 and 6 rather than adjusting totals locally.

---

## 10. Quick reference — new or changed endpoints

| Method | Route | Notes |
| --- | --- | --- |
| `GET` | `/api/v1/checkout/summary` | Authoritative quote; new |
| `POST` | `/api/v1/cart/coupon` | Apply; returns recalculated summary; new |
| `DELETE` | `/api/v1/cart/coupon` | Remove; idempotent; new |
| `GET` | `/api/v1/products/{productId}/attributes` | New |
| `PUT` | `/api/v1/products/{productId}/attributes` | New; full replace |
| `DELETE` | `/api/v1/products/{productId}/attributes/{attributeId}` | New |
| `POST` | `/api/v1/products/{productId}/variants` | `sortOrder`, `attributeValueIds` now optional |
| `GET` | `/api/v1/store/products/{slug}` | Variants now include resolved attributes |

All other routes and response shapes are unchanged.

---

## Questions

For anything not covered here, the behaviour is defined by the tests. The suites are the
executable specification:

- `tests/KromicCommerce.UnitTests/Application/CheckoutSummaryPricingTests.cs` — pricing and readiness
- `tests/KromicCommerce.UnitTests/Application/OrderCancellationRefundTests.cs` — refund-before-cancel ordering
- `tests/KromicCommerce.UnitTests/Application/EmailSettingsValidationTests.cs` — mode-conditional validation
- `tests/KromicCommerce.UnitTests/Domain/CashOnDeliveryConfigurationTests.cs` — COD fee semantics
- `tests/KromicCommerce.UnitTests/Application/StorefrontCacheInvalidationTests.cs` — cache correctness
