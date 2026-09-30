# Frontend Payment Integration Guide

> **Who is this for:** Frontend engineers integrating the Kromic Commerce checkout and Razorpay payment flow.
>
> **Backend already done:** Razorpay credentials are configured in the Admin panel and stored in the database. The backend handles all pricing, signature verification, and order state — the frontend never calculates or sends any amounts.

---

## Overview

```
App boot                  Checkout page            After widget closes
───────────────           ─────────────────────    ──────────────────────────
GET /store/settings  →    POST /checkout       →   Razorpay widget opens
  read payment.razorpayEnabled   read providerOrderId        user pays
  read payment.razorpayKeyId     read razorpayKeyId           ↓
  read payment.codEnabled        read grandTotal          POST /payments/verify
```

There are exactly **3 API calls** involved. Everything else is handled server-side.

---

## Step 0 — Load store settings at app boot

Call this once when the app initialises (or when the checkout page mounts). Cache the result for the session.

```
GET /api/v1/store/settings
```

No authentication required.

**Response (relevant section):**

```json
{
  "payment": {
    "razorpayEnabled": true,
    "codEnabled": true,
    "razorpayKeyId": "rzp_live_xxxxxxxxxxxx"
  },
  "delivery": {
    "codEnabled": true,
    "codExtraFee": 30.00,
    ...
  },
  ...
}
```

| Field | What to do with it |
|---|---|
| `payment.razorpayEnabled` | Show/hide the "Pay Online" option. If `false`, hide it entirely — do not show a disabled state. |
| `payment.razorpayKeyId` | Store this. You will pass it to the Razorpay widget in step 2. **Never hardcode a key.** |
| `payment.codEnabled` | Show/hide the "Cash on Delivery" option. |

> `payment.razorpayEnabled` is `true` only when the admin has saved credentials **and** toggled the integration on. Both conditions must be met.

---

## Step 1 — POST /checkout

When the user submits the checkout form, call this endpoint. **Do not send prices, discounts, shipping costs, or totals.** The server computes everything.

```
POST /api/v1/checkout
Authorization: Bearer <access_token>
Content-Type: application/json
```

**Request body:**

```json
{
  "addressId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "paymentMethod": "Razorpay",
  "couponCode": null,
  "idempotencyKey": "a3f9c1d2-0e4b-4c7a-8f1e-2b3d5e6a7c8f"
}
```

**Field notes:**

- `addressId` — ID of the selected saved address from `GET /api/v1/customer/addresses`. It must belong to the authenticated customer and include a phone number.
- `paymentMethod` — enum string, exactly `"Razorpay"` or `"CashOnDelivery"`. Case-sensitive.
- `couponCode` — `null` if no coupon was applied.
- `idempotencyKey` — generate a fresh `uuidv4` per checkout attempt and store it locally. If the request times out or the network drops, **resend the exact same key** — the server returns the original order instead of creating a duplicate.

**Success response `201 Created`:**

```json
{
  "orderId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "orderNumber": "ORD-20260929-A3F9C1",
  "orderStatus": "PendingPayment",
  "paymentMethod": "Razorpay",
  "subtotal": 1999.00,
  "shippingAmount": 49.00,
  "codFee": 0.00,
  "discountAmount": 200.00,
  "taxAmount": 324.00,
  "grandTotal": 2172.00,
  "currency": "INR",
  "appliedCouponCode": "SAVE10",
  "providerOrderId": "order_NMm8GjxXRx4bJF",
  "razorpayKeyId": "rzp_live_xxxxxxxxxxxx",
  "createdAtUtc": "2026-09-29T10:30:00Z"
}
```

**Fields you must save before opening the widget:**

| Field | Required for |
|---|---|
| `orderId` | `POST /payments/verify` query param |
| `providerOrderId` | Razorpay widget `order_id` option |
| `razorpayKeyId` | Razorpay widget `key` option |
| `grandTotal` | Razorpay widget `amount` (multiply × 100 for paise) |
| `currency` | Razorpay widget `currency` option |

> If `providerOrderId` is `null` on a Razorpay order, the Razorpay API call failed on the backend. Do not open the widget. Show: *"Payment initialisation failed. Please try again."* The order can be retried — send a fresh POST /checkout with the same `idempotencyKey`.

**For COD orders** — `providerOrderId` and `razorpayKeyId` are always `null`. `orderStatus` is `"OrderPlaced"`. You're done after this call; skip steps 2 and 3.

---

## Step 2 — Open the Razorpay widget

Use the values from the step 1 response — do not use any locally calculated totals.

```js
const options = {
  key: checkoutResponse.razorpayKeyId,           // from POST /checkout response
  amount: Math.round(checkoutResponse.grandTotal * 100), // paise — use server total
  currency: checkoutResponse.currency,
  name: storeSettings.businessName,             // from GET /store/settings
  description: checkoutResponse.orderNumber,
  order_id: checkoutResponse.providerOrderId,   // from POST /checkout response
  handler: function (rzpResponse) {
    // Step 3 — called automatically when payment succeeds in the widget
    verifyPayment({
      orderId: checkoutResponse.orderId,
      razorpayPaymentId: rzpResponse.razorpay_payment_id,
      razorpayOrderId: rzpResponse.razorpay_order_id,
      razorpaySignature: rzpResponse.razorpay_signature,
    });
  },
  modal: {
    ondismiss: function () {
      // User closed the widget without paying.
      // Order is in PendingPayment state. Let user retry or cancel.
      showRetryOption();
    }
  },
  prefill: {
    name: `${selectedAddress.firstName} ${selectedAddress.lastName}`,
    contact: selectedAddress.phone,
  },
  theme: { color: "#3399cc" }
};

const rzp = new Razorpay(options);
rzp.open();
```

Load the Razorpay script once in your HTML:

```html
<script src="https://checkout.razorpay.com/v1/checkout.js"></script>
```

---

## Step 3 — POST /payments/verify

Call this immediately inside the widget's `handler` callback. **Do not mark the order as paid on the frontend** — only do that after this call returns `200`.

```
POST /api/v1/payments/verify?orderId=<orderId-from-step-1>
Authorization: Bearer <access_token>
Content-Type: application/json
```

**Request body** — all three values come directly from the Razorpay widget `handler` argument:

```json
{
  "razorpayPaymentId": "pay_NMm8XXXXXXXYYYY",
  "razorpayOrderId":   "order_NMm8GjxXRx4bJF",
  "razorpaySignature": "abc123def456..."
}
```

**Success response `200 OK`:**

```json
{
  "orderId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "orderNumber": "ORD-20260929-A3F9C1",
  "status": "Confirmed",
  "paidAt": "2026-09-29T10:31:15Z"
}
```

Navigate the user to the order confirmation page. The order is now confirmed and inventory is committed.

**Failure response `400` / `404`:** The signature check failed or the order was not found. Show: *"Payment could not be verified. Please contact support with your order number."* Do not let the user retry silently — the payment may have already been captured by Razorpay.

---

## Error handling reference

All errors follow the same envelope:

```json
{
  "error": {
    "code": "INSUFFICIENT_INVENTORY",
    "message": "Only 2 units of 'Blue T-Shirt (M)' are available."
  }
}
```

| Code | When | What to show |
|---|---|---|
| `CART_EMPTY` | POST /checkout | Redirect to cart page |
| `COD_NOT_AVAILABLE` | POST /checkout | Hide COD, tell user to use online payment |
| `PRODUCT_UNAVAILABLE` | POST /checkout | Refresh cart — an item was removed or deactivated |
| `INSUFFICIENT_INVENTORY` | POST /checkout | Show "X units available" for the affected item |
| `COUPON_INVALID` | POST /checkout | Show the `message` field from the error — it is user-friendly |
| `COUPON_USAGE_LIMIT` | POST /checkout | "This coupon has reached its usage limit" |
| `401 Unauthorized` | Any | Token expired — refresh and retry |
| `409 Conflict` | POST /checkout (idempotency) | Order already exists — navigate to that order |

---

## Checklist before going live

- [ ] `payment.razorpayEnabled` from store settings drives whether the online payment option is shown — not a hardcoded flag.
- [ ] `razorpayKeyId` is read from the store settings or checkout response — never hardcoded.
- [ ] `order_id` in the widget options is `providerOrderId` from the checkout response.
- [ ] `amount` in the widget options is `grandTotal * 100` (paise) from the checkout response — not your own calculated total.
- [ ] `POST /payments/verify` is always called after the widget `handler` fires.
- [ ] The order is only treated as confirmed after `POST /payments/verify` returns `200`.
- [ ] A fresh `idempotencyKey` (uuidv4) is generated per checkout attempt and reused on retry.
- [ ] Widget `ondismiss` is handled — user should be able to retry payment without re-placing the order.
- [ ] For COD orders, there is no Razorpay widget and no verify call — the order is placed and you navigate directly to confirmation.

---

## Sequence diagram

```
Frontend                   Backend                     Razorpay
   │                           │                           │
   │── GET /store/settings ──▶ │                           │
   │◀─ { payment.razorpayEnabled,                          │
   │     payment.razorpayKeyId } ─                         │
   │                           │                           │
   │── POST /checkout ────────▶│                           │
   │                           │── Create Razorpay order ─▶│
   │                           │◀─ providerOrderId ────────│
   │◀─ { providerOrderId,      │                           │
   │     razorpayKeyId,        │                           │
   │     grandTotal, orderId } │                           │
   │                           │                           │
   │── open Razorpay widget ──────────────────────────────▶│
   │                           │                           │ user pays
   │◀─ handler({ payment_id,   │                           │
   │     order_id, signature })──────────────────────────── │
   │                           │                           │
   │── POST /payments/verify ─▶│                           │
   │                           │── verify HMAC signature   │
   │                           │── confirm order           │
   │◀─ { status: "Confirmed" } │                           │
   │                           │                           │
   │── navigate to confirmation│                           │
```
