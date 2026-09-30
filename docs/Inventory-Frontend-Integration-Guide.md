# Inventory & Stock — Frontend Integration Guide

**Backend:** `KromicCommerce` — ASP.NET Core 8, CQRS + MediatR, EF Core, PostgreSQL
**Scope:** Stock availability, out-of-stock state, and the order/cancellation stock lifecycle
**Audience:** Frontend engineer building product cards, variant selectors, admin stock UI, and checkout

---

## 0. Read this first: most of this already existed

The backend already had a complete inventory lifecycle before this work. **No new stock column
was added and no second inventory system was introduced.** Stock lives in exactly one place:

```
InventoryItem  (ProductId, VariantId, OnHand, Reserved, LowStockThreshold)
  VariantId = null  → base product stock (product with no variants)
  VariantId = set   → that variant's own stock
```

`Product` and `ProductVariant` deliberately carry **no stock field**. If you were expecting a
`product.stockQuantity` write path, there isn't one — you set stock through the inventory
endpoints below.

This guide documents the lifecycle, the **three behavioural changes** in §2, and the fields
available for display.

---

## 1. The stock model

### Each unit is in one of three states

| State | OnHand | Reserved | Meaning |
| --- | --- | --- | --- |
| **Available** | counted | not counted | sellable now |
| **Reserved** | counted | counted | held for an open order (reserved at checkout) |
| **Sold** | removed | removed | consumed by a confirmed order |

`Available = OnHand − Reserved` is computed, never stored.

### The lifecycle

```
Admin sets stock  ─────────────────────► Available
Add to cart      ─────────────────────► Available   (cart NEVER reserves)
Checkout         ── Reserve(qty) ─────► Reserved
Admin confirms   ── Finalize(qty) ─────► Sold       (stock decreases here)
Order cancelled  ── Restore(qty)  ─────► Available  (stock returns here)
```

**Stock is not reduced at checkout.** Checkout only *reserves*. The actual decrease happens when
an admin confirms the order — for both COD and Razorpay, which share one consumption point.

### `IsOutOfStock` is derived, never stored

```
IsOutOfStock  ⇔  Available <= 0
```

There is no `isOutOfStock` database column, and no admin "Out of Stock" toggle. Setting stock to
`0` makes a product out of stock; setting it above `0` makes it available. The contradictory
state (`stock = 10` + `isOutOfStock = true`) is not representable.

For a product with variants the parent is out of stock **only when every sellable variant is**:

```
128GB → 10   256GB → 0   512GB → 5
parent isOutOfStock = false      (512GB is available)

128GB → 0    256GB → 0   512GB → 0
parent isOutOfStock = true
```

---

## 2. Behavioural changes in this update

Three changes you should be aware of. All are bug fixes that make the backend match what the
lifecycle above describes.

### 2.1 Cancelling a *confirmed* order now returns its stock

**Before:** cancelling a confirmed order issued the refund, marked the order cancelled, and
silently left the units out of stock. Every cancel-after-confirm permanently shrank sellable
inventory.

**Now:** the units are returned. See §5 for what this means for a cancelled order's stock.

### 2.2 Stock consumption is all-or-nothing

**Before:** if one line of a multi-line order could not be consumed, the failure was logged and
skipped, and the order was still marked `Confirmed` — with some of its stock consumed and no way
to ever repair it.

**Now:** if any line cannot be consumed, confirmation is abandoned. The order stays unconfirmed
and nothing is consumed, so an admin can resolve the shortfall and retry.

### 2.3 New error code: `INVENTORY_RESTORE_FAILED`

Cancellation can now fail with `409` and code `INVENTORY_RESTORE_FAILED`. The order stays
**unchanged** and cancellable, so the operation can be retried. Treat it like `REFUND_FAILED`:
show the message, do not optimistically mark the order cancelled.

---

## 3. Fields available to your UI

### Storefront product — `GET /api/v1/store/products/{slug}` and list endpoints

```ts
{
  "id": "…",
  "name": "…",
  "price": 1299.00,
  "currency": "INR",

  "stockAvailability": "InStock" | "LowStock" | "OutOfStock",
  "isOutOfStock": false,          // NEW — derived from stockAvailability
  "canPurchase": true,

  "variants": [
    {
      "id": "…",
      "effectivePrice": 1299.00,
      "isActive": true,
      "stockAvailability": "InStock",
      "isOutOfStock": false,      // NEW
      "canPurchase": true
    }
  ]
}
```

`isOutOfStock` is **new** and is derived from `stockAvailability`. It adds no new information —
it just saves you comparing enum members. Both fields are always consistent.

⚠️ **Exact unit counts are not exposed on storefront responses.** The existing public projection
deliberately omits raw stock counts. If you need a quantity, use `stockAvailability`. For a
numeric count, use the admin inventory endpoint (admin token only).

### Admin variant — `GET /api/v1/products/{productId}/variants`

```ts
{
  "id": "…",
  "sku": "…",
  "priceOverride": null,
  "sortOrder": 0,
  "isActive": true,
  "attributeValueIds": "…",
  "availableStock": 10,      // numeric count, admin only
  "isOutOfStock": false      // NEW — derived from availableStock
}
```

`isOutOfStock` here is derived from `availableStock`. A `null` `availableStock` means the variant
has no inventory record (not stock-tracked) and is reported as **not** out of stock.

### Admin inventory

The two write endpoints below return this shape (unchanged), and it is the **only** place
numeric counts are available — it is admin-only.

```ts
{
  "id": "…", "productId": "…", "variantId": null,
  "onHand": 12, "reserved": 2, "available": 10,
  "lowStockThreshold": 5,
  "isLowStock": false,
  "isOutOfStock": false,
  "updatedAt": "…"
}
```

There is **no** `GET` on the inventory controller — it is write-only. To prefill the admin stock
input, use `availableStock` from the variants endpoint (§3) for variants, or
`onHand`/`available` from the product's own detail response if it carries them. The write
endpoints return the full `InventoryResponse` above, so you always get the numbers back after a
save.

---

## 4. Admin stock management

⚠️ **Both routes are under `/api/v1/admin/inventory`, and `variantId` is a QUERY parameter, not
a body field.**

### Set absolute stock — `PUT /api/v1/admin/inventory/{productId}?variantId={variantId}`

```jsonc
// simple product  — PUT /api/v1/admin/inventory/{productId}
// body:
{ "onHand": 10, "lowStockThreshold": 5 }

// a specific variant — PUT /api/v1/admin/inventory/{productId}?variantId=<guid>
// body:
{ "onHand": 0, "lowStockThreshold": 5 }
```

Omit `variantId` for a product with no variants. `onHand` is required; `lowStockThreshold`
defaults to `5`.

Sets `OnHand` absolutely and **creates the inventory row if it does not exist** — so this is also
how you start tracking a previously untracked product. Returns the full `InventoryResponse`,
including the recomputed `isOutOfStock`. Set `onHand: 0` to mark out of stock — no separate
toggle.

### Adjust by delta — `POST /api/v1/admin/inventory/{productId}/adjust?variantId={variantId}`

```jsonc
// body:
{ "delta": 5, "reason": "restock delivery" }
```

Note: **POST**, not PUT. `delta` is required; `reason` is optional and stored for audit.
Positive to increase, negative to reduce. Returns the full `InventoryResponse`.

This endpoint requires an existing inventory row — call the `PUT` first for a new product
(otherwise `404 INVENTORY_NOT_FOUND`).

### UI

```
Simple product
  Stock Quantity  [ 10 ]      ← prefill from the product's own availableStock
  In Stock                     ← derived, display only

Variant
  128GB    Stock Quantity [10]   In Stock     ← prefill from variants[].availableStock
  256GB    Stock Quantity [ 0]   Out of Stock
  512GB    Stock Quantity [ 5]   In Stock
```

Stock belongs to **each variant**. A product with variants has no single quantity to edit —
edit the variants. There is no separate "Out of Stock" checkbox.

**Never clear or refresh a cache from the admin UI.** Both endpoints invalidate the affected
product projections server-side, so the next storefront request returns fresh values.

### Validation and errors

| Condition | Status | Code |
| --- | --- | --- |
| `onHand` negative | `400` | validation error |
| `onHand` below currently reserved | `400` | `STOCK_ADJUSTMENT_INVALID` |
| Adjustment would go negative | `400` | `STOCK_ADJUSTMENT_INVALID` |
| Product not found | `404` | `PRODUCT_NOT_FOUND` |
| No inventory row for `adjust` | `404` | `INVENTORY_NOT_FOUND` |

Stock is a whole-unit integer. Decimal quantities are not representable and are rejected.

---

## 5. Order, stock, and cancellation

### Stock does not change at checkout

Checkout **reserves**. A customer can hold stock while deciding. `availableStock` reflects
reserved units already being held. If checkout fails (payment not completed, order abandoned),
the reservation is released.

### Stock is consumed at admin confirmation

The admin confirms an order via:

```
PUT /api/v1/admin/orders/{id}/status
{ "status": "Confirmed" }
```

This is the **only** point where `OnHand` decreases, and it is identical for COD and Razorpay.
Both payment methods reserve at checkout and consume at this transition, so they cannot drift
apart.

On success, all stock-dependent projections are invalidated automatically.

On failure (a line's reservation is short), you receive `409`
`INVALID_ORDER_TRANSITION` and the order is **unchanged** — nothing was consumed. The error
message names the product and the shortfall. Resolve the stock and retry the confirmation.

### Cancellation returns stock

When an order is cancelled — by the customer or by an admin — its units return to sellable stock:

| Order was | Units were | On cancellation |
| --- | --- | --- |
| never confirmed | `Reserved` | released back to `Available` |
| confirmed | `Sold` | added back to `OnHand` |

This is why a cancelled order always returns its stock, and why a confirmed-then-cancelled order
no longer leaks inventory.

For a Razorpay order, the refund must succeed **before** any stock is returned. A failed refund
returns `409 REFUND_FAILED` with the order and stock both unchanged. Stock is never returned for
an order whose refund did not go through.

Cancellation is idempotent: a retry cannot return the same units twice.

### Error contract

| Code | Status | Meaning for your UI |
| --- | --- | --- |
| `REFUND_FAILED` | `409` | Order unchanged, stock unchanged. Show message; do not mark cancelled. |
| `INVENTORY_RESTORE_FAILED` | `409` | Order unchanged, stock unchanged. Show message; allow retry. |
| `INVALID_ORDER_TRANSITION` | `409` | Wrong state (e.g. confirming twice, cancelling a shipped order). |

In all three cases the order is genuinely still in its previous state. **Never optimistically
apply the state change on a `409`.**

A successful cancellation means the refund was **accepted** by the provider, not that funds have
landed (typically 5–7 business days).

---

## 6. Checkout and stale stock

Stock is revalidated at checkout, so a page rendered earlier may be out of date. This is
intentional and correct — checkout is the authority.

If stock changed between the customer viewing a product and checking out:

| Code | Status | Meaning |
| --- | --- | --- |
| `INSUFFICIENT_INVENTORY` | `409` | Not enough units to fulfil the cart |
| `CART_EMPTY` | `400` | Cart expired or empty |

The checkout summary also reports availability before you submit, via each line's
`canPurchase` and `stockAvailability`, plus a blocking reason:

| Blocking code | Meaning |
| --- | --- |
| `INSUFFICIENT_STOCK:<id>` | Not enough stock for a cart line |
| `PRODUCT_UNAVAILABLE:<id>` | Product archived or removed |
| `VARIANT_UNAVAILABLE:<id>` | Variant gone or inactive |

The last three are prefix-plus-id. Match on the prefix; use the id to highlight the line.

On an insufficient-stock error:

1. Show a clear message — e.g. "Some items in your cart are no longer available in the
   requested quantity."
2. Refetch the checkout summary (or cart) to get current state.
3. Refetch the affected product/variant so the card reflects reality.
4. Prevent the customer from continuing at the unavailable quantity.

Do not calculate availability yourself or retry blindly — refetch, then let the customer choose.

---

## 7. Cache and refetch expectations

You never clear caches. The backend invalidates affected projections after every successful
stock mutation — manual admin update, order confirmation, and cancellation alike.

| What changed | What is invalidated |
| --- | --- |
| Admin sets product stock | product, storefront product, stock-dependent projections |
| Admin sets variant stock | variant **and parent product** (the parent may flip to out of stock), storefront product |
| Order confirmed | same as variant/product stock change |
| Order cancelled | same |

A variant stock change invalidates the **parent product** too, because the parent's
`isOutOfStock` is a rollup over its variants. When the last variant hits zero, the parent flips
to out of stock and that must be reflected.

Brand and category counts are **not** invalidated by stock changes — they count products, not
stock. You do not need to refetch brand/category on a stock change.

Refetch after:
- placing an order
- cancelling an order
- an insufficient-stock error
- an admin stock edit (if you display storefront availability in the same session)

Availability may lag very briefly between the change and your next fetch. That is a cache
timing detail, not a correctness issue — checkout always revalidates.

---

## 8. Frontend product card

```ts
// ❌ wrong — frontend-side stock arithmetic and derived state
if (stock - cartQty > 0) enableAddToCart();

// ✅ correct — backend is authoritative
const buyable = product.canPurchase && !product.isOutOfStock;
```

| `isOutOfStock` | Behaviour |
| --- | --- |
| `true` | Show "Out of Stock", disable Add to Cart and Buy Now, keep the product visible |
| `false` | Normal purchase UI |

Keep the product visible when out of stock unless your catalog design deliberately hides
unavailable products. Note `canPurchase` is stricter than `!isOutOfStock`: it is also `false`
for draft/archived products and inactive variants, so use it as the single purchase gate.

## 9. Frontend variant selector

```
128GB   isOutOfStock=false   → selectable
256GB   isOutOfStock=true    → disabled, "Out of Stock"
512GB   isOutOfStock=false   → selectable
```

- Disable unavailable options; do not hide them, so customers can see the option exists.
- Gate the purchase action on the **selected** variant's `isOutOfStock` / `canPurchase`, not the
  parent's.
- If the selected variant becomes unavailable after a refetch, prevent purchase and ask the
  customer to choose an available one.

## 10. Frontend order flow

```
Customer checks out
   ↓
Backend reserves stock (no decrease yet)
   ↓
Order placed — stock unchanged from the customer's view
   ↓
Payment / admin confirmation
   ↓
Backend consumes stock, invalidates caches
   ↓
Frontend refetches affected product data if it displays availability
```

**Never decrement stock client-side after an order.** The backend owns inventory. Refetch the
affected product or checkout summary when you need current numbers.

---

## 11. Migration notes for existing data

No schema change was made to stock. `InventoryItem` was already the store of record, so:

- Existing inventory rows are untouched.
- Products created **before** stock tracking have **no inventory row**. For those, the storefront
  reports them as in stock and purchasable (no tracking), and admin confirmation has nothing to
  consume. This is the pre-existing, deliberate behaviour and is unchanged.
- An admin can start tracking a product by calling
  `PUT /api/v1/admin/inventory/{productId}`, which creates the row.
  creates the row.

No fake default stock was assigned to existing products.

---

## 12. Integration checklist

- [ ] Read availability from `isOutOfStock` / `stockAvailability`; never compute it.
- [ ] Never decrement or increment stock client-side.
- [ ] Never maintain a separate frontend "out of stock" flag.
- [ ] Gate Add to Cart / Buy Now on `canPurchase` (stricter than `!isOutOfStock`).
- [ ] Disable out-of-stock variants; don't hide them.
- [ ] Gate purchase on the *selected* variant, not the parent.
- [ ] Admin: edit per-variant `onHand`; no separate out-of-stock toggle; prefill from the
      product/variant `availableStock` already in the response.
- [ ] On `INSUFFICIENT_INVENTORY`, refetch checkout/cart **and** affected product, then show the error.
- [ ] On `409` (any code) from confirm/cancel, do **not** optimistically apply the state change.
- [ ] Handle new `INVENTORY_RESTORE_FAILED` like `REFUND_FAILED`.
- [ ] Refetch product data after placing/cancelling an order if you show availability.
- [ ] Don't refetch brand/category on stock changes (counts don't depend on stock).
- [ ] Don't try to clear caches from the UI.

---

## 13. Where the behaviour is pinned by tests

| Test file | Covers |
| --- | --- |
| `Domain/InventoryItemTests.cs` | Reserve/Finalize/Restore state machine, negative-stock rejection, derived `IsOutOfStock`, variant independence, no double-apply |
| `Application/OrderConfirmationStockTests.cs` | Consumption at confirmation, variant vs base row, all-or-nothing, idempotency, no persist on failure |
| `Application/OrderCancellationRefundTests.cs` | Restore of confirmed vs unconfirmed orders, refund-before-restore, failed-restore leaves order live |
| `Application/CheckoutSummaryPricingTests.cs` | Insufficient stock blocks checkout |
| `IntegrationTests/…/PaymentRefundSchemaTests.cs` | xmin concurrency token rejects a stale write; `NULLS NOT DISTINCT` inventory uniqueness |
