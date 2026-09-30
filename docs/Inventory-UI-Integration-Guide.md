# UI Integration Guide — Stock & Inventory

Scope: every endpoint the frontend needs for **stock, inventory and the order flows that move
stock**. The API surface described here was **not changed** by the inventory-hardening work — see
[What changed for the UI](#what-changed-for-the-ui) for the three behaviours to handle.

---

## What changed for the UI

**No endpoint, request field, or response field was added, removed, or renamed.** The
per-order-item inventory lifecycle introduced for idempotent cancellation is internal state and
appears in **no** API contract (`OrderItemResponse` is byte-for-byte unchanged).

Three behaviours are worth handling, none of which require new integration work:

| # | Behaviour | What the UI should do |
|---|---|---|
| 1 | **409 `CONCURRENCY_CONFLICT`** can now come back from cancel and status-change when two requests race. This is new on those endpoints. | Treat 409 as *retryable*: show "someone else updated this order, refreshing…", refetch, let the user retry. Do **not** show it as a fatal error. |
| 2 | Stock availability now refreshes correctly after an order. Previously the cached `IsOutOfStock` could stay stale after placing/cancelling an order. | Refetch stock after `POST /orders`, `POST /orders/{id}/cancel`, `PUT /admin/orders/{id}/status`, `POST /payments/verify`. This is a **fix**, not a breaking change — previously your UI was showing wrong data. |
| 3 | `409 INVENTORY_RESTORE_FAILED` — cancellation refused because stock could not be returned. The order is **not** cancelled. | Surface as a clear operational error. The admin must reconcile stock before retrying. |

### ⚠️ Pre-existing orders have no automatic stock restoration

Order lines created **before** the migration are marked `Untracked`, so cancelling them will **not**
return their units to sellable stock. This is deliberate — backfilling from current stock counters
is the unsound inference the change removed. If your store has orders placed before the deploy,
their lines need manual stock reconciliation on cancellation. New orders are unaffected.

---

## 0. Admin stock management — the full flow

### What the backend already supports

The **write** side of admin stock management is complete and has been for some time. Nothing was
added by the inventory-hardening work, and nothing is missing:

| Capability | Endpoint | Status |
|---|---|---|
| Set absolute on-hand count | `PUT /api/v1/admin/inventory/{productId}` | ✅ exists |
| Adjust by delta (+/−) | `POST /api/v1/admin/inventory/{productId}/adjust` | ✅ exists |
| Read stock per variant | `GET /api/v1/admin/products/{id}` → `variants[].availableStock` | ✅ exists |

So an admin stock screen can be built **today** with no backend work. The gap is on the **read/list**
side — see [Known gap](#known-gap-no-stock-list-screen-yet).

### Screen: Stock

```
┌─ Stock ─────────────────────────────────────────────────────────────────┐
│ [Product search / SKU…]        [ In stock only ▾ ]        [ Refresh ]     │
├─────────────────────────────────────────────────────────────────────────┤
│ Product            Variant    On hand  Reserved  Available  Status  …    │
│ Widget             W-128        50        3         47       In stock    │
│ Widget Pro         256GB        12        0         12       In stock    │
│ Gadget             —             4        4          0       Out of stock│
│ Legacy Item        —            null      —          —       Not tracked  │
│                                            [ Edit ]  [ Adjust ]         │
└─────────────────────────────────────────────────────────────────────────┘
```

### The editing flow, end to end

**Step 1 — open the editor.** The row is populated from the admin product payload:

```jsonc
// GET /api/v1/admin/products/{id}   →  product.variants[]
{
  "id": "b2c3...",
  "sku": "W-128",
  "priceOverride": null,
  "sortOrder": 0,
  "isActive": true,
  "availableStock": 47
}
```

> The list endpoint `GET /api/v1/admin/products` returns a **summary with no stock field**. You
> must fetch the product detail to read `availableStock`. See [Known gap](#known-gap-no-stock-list-screen-yet).

**Step 2 — edit.** Two modes:

| Mode | Use when | Endpoint | Body |
|---|---|---|---|
| **Absolute set** | Stock take, correcting a count | `PUT /api/v1/admin/inventory/{productId}?variantId=` | `{ "onHand": 50, "lowStockThreshold": 5 }` |
| **Adjust by delta** | Receiving a delivery, removing damaged units | `POST /api/v1/admin/inventory/{productId}/adjust?variantId=` | `{ "delta": 25, "reason": "PO-1024 received" }` |

`variantId` is a **query parameter**. Omit it entirely for a base-product row.

Prefer **Adjust** for anything incremental — it is auditable via `reason` and cannot silently
overwrite a concurrent change the way an absolute set can.

**Step 3 — submit.** Both return `200` with the full `InventoryResponse`. **Replace the whole row
from that response** — do not optimistically compute the new numbers. The server applies the
`onHand`/`reserved` invariants and derives `available`, `isLowStock` and `isOutOfStock` itself.

**Step 4 — invalidate.** On success, refetch the admin product detail and any storefront view of
that product. The backend evicts its own cache after committing; the UI must drop its copy too.

### Validation the server enforces — mirror it in the form

| Rule | Server response | Form guidance |
|---|---|---|
| `onHand` cannot be negative | `400` | Min `0` on the input |
| `onHand` cannot drop below `reserved` | `400` | Show current `reserved` inline; the floor is `reserved`, not `0` |
| `delta` required on adjust | `400` | Disable submit while empty |
| `delta` cannot drive stock negative | `400` | Clamp to `−available` |
| Unknown product | `404 PRODUCT_NOT_FOUND` | — |
| Untracked product | `404` | Direct the admin to the inventory-tracked flow first |

A `400` here is a **validation** failure, not a transient error — surface it inline on the field.
Do not retry it.

### Status rendering

| Condition | Badge |
|---|---|
| `available <= 0` | **Out of stock** (red) |
| `0 < available <= lowStockThreshold` | **Low stock** (amber) |
| `available > lowStockThreshold` | **In stock** (green) |
| `availableStock === null` | **Not tracked** (grey) — *not* "Out of stock" |

`isLowStock` and `isOutOfStock` come pre-computed from the server. Don't recompute them, and don't
treat `null` as `0`.

### Base product vs variant

A product has **either** a base inventory row (`variantId: null`) **or** per-variant rows — never
both. The UI must present whichever the backend returns:

- `variants: []` → edit the base row; omit `variantId`
- `variants: [...]` → one editable row per variant; pass each `variantId`

Never show a base row *and* variant rows together — `InventoryItem` is unique on
`(ProductId, VariantId)`, and editing both would double-count stock.

---

## Known gap: no stock list screen yet

The update flow above works, but a **dedicated stock list** cannot be built efficiently today:

| Missing | Consequence for the UI |
|---|---|
| **No inventory list endpoint.** | `GET /api/v1/admin/products` returns a summary with **no stock field**; `availableStock` exists only on the **detail** payload. A stock table therefore costs **1 + N requests** (list, then one detail per product). Unusable at 20+ products. |
| **No low-stock filter.** | `ProductQueryRequest` supports `InStockOnly` only. "Show me everything low" must be filtered client-side across the whole catalogue. |
| **No stock sorting.** | Allowed `sortBy` values are `name`, `price`, `created_at`, `updated_at`. You cannot sort by availability. |
| **No inventory search by SKU.** | `search` targets the product, not the variant SKU. |

**Workaround until this exists:** build stock editing *per product* — from the product detail page,
read `variants[].availableStock` and edit in place. That path needs no new backend and works
today. Do not attempt a global stock table.

**If you want a real stock list screen**, the minimal backend addition is a single paginated
endpoint returning one row per inventory item with product/variant identity, `available`, and the
low-stock filter — `GET /api/v1/admin/inventory?page=&pageSize=&search=&lowStockOnly=`. Say the
word and I'll build it; it is additive and breaks nothing.

---

## 1. Admin stock endpoints

Base: `/api/v1/admin/inventory` · Auth: **Admin only**

> There is **no GET endpoint** for inventory. Read current stock from the admin product endpoints
> (§3), which embed it per variant.

### Set absolute stock

```
PUT /api/v1/admin/inventory/{productId}?variantId={variantId}
```

```json
{ "onHand": 50, "lowStockThreshold": 5 }
```

| Field | Type | Notes |
|---|---|---|
| `onHand` | int | Absolute physical count. `lowStockThreshold` defaults to `5` if omitted. |
| `lowStockThreshold` | int? | Optional. Defaults to `5`. |

`variantId` is a **query parameter**, omitted for base-product stock. Rejected with 400 if
`onHand` is negative or below the currently `reserved` count.

**200** → `InventoryResponse`

### Adjust stock by delta

```
POST /api/v1/admin/inventory/{productId}/adjust?variantId={variantId}
```

```json
{ "delta": -3, "reason": "damaged in transit" }
```

| Field | Type | Notes |
|---|---|---|
| `delta` | int | Positive = restock, negative = manual reduction. **Required.** |
| `reason` | string? | Optional audit note. |

Rejected with 400 if the result would go negative or drop below `reserved`.

### InventoryResponse (both endpoints)

```json
{
  "id": "3f1c...",
  "productId": "a1b2...",
  "variantId": null,
  "onHand": 50,
  "reserved": 3,
  "available": 47,
  "lowStockThreshold": 5,
  "isLowStock": false,
  "isOutOfStock": false,
  "updatedAt": "2026-09-30T14:20:43Z"
}
```

**Read `available`, not `onHand`.** `available = onHand - reserved`. Showing `onHand` to customers
oversells whenever another order holds a reservation.

| Flag | Meaning |
|---|---|
| `isOutOfStock` | `available <= 0` |
| `isLowStock` | `available <= lowStockThreshold` **and** `available > 0` |

---

## 2. Where stock appears in other payloads

### Admin variant — `GET /api/v1/admin/products/{id}`

```json
{
  "id": "...",
  "name": "Widget",
  "variants": [
    { "id": "...", "sku": "W-128", "priceOverride": null,
      "sortOrder": 0, "isActive": true, "availableStock": 47 }
  ]
}
```

> **`availableStock` is `int?`.** `null` means the variant has **no inventory record** — it is not
> stock-tracked (a product created before inventory tracking existed). `null` is **not** zero.
> `VariantResponse.isOutOfStock` is computed as `availableStock.HasValue && availableStock <= 0`,
> so `null` correctly reports **not** out of stock. Render untracked as "not tracked", never as 0.

### Storefront — derived, never raw counts

`GET /api/v1/store/products`, `GET /api/v1/store/products/{slug}`,
`GET /api/v1/store/featured`, `GET /api/v1/store/products/{slug}/related`

```json
{ "stockAvailability": "InStock" }
```

| Value | Meaning |
|---|---|
| `InStock` | Purchasable |
| `LowStock` | At or below threshold |
| `OutOfStock` | No availability |

Storefront responses **never** expose `onHand`/`reserved`. `isOutOfStock` is **derived** from
`stockAvailability`, so the contradictory pair `stockAvailability: InStock` + `isOutOfStock: true`
cannot occur.

Variant rollup rule: a product is out of stock only when **all** its variants are. One purchasable
variant makes the product purchasable.

### Filtering

| Endpoint | Parameter |
|---|---|
| `GET /api/v1/admin/products` | `inStockOnly` (bool?) |
| `GET /api/v1/store/products` | `inStockOnly` (bool, default `false`) |

---

## 3. Order endpoints that move stock

All require an authenticated user; the cancel endpoint additionally checks ownership.

| Method | Route | Auth | Moves stock? |
|---|---|---|---|
| `POST` | `/api/v1/orders/{id}/cancel` | Owner | **Yes** — restores on success |
| `PUT` | `/api/v1/admin/orders/{id}/status` | Admin | **Yes** — only when setting `Confirmed` |
| `POST` | `/api/v1/checkout` | Customer | **Yes** — reserves |
| `POST` | `/api/v1/payments/verify` | Customer | **Yes** — releases if payment fails |

### Cancel an order — `POST /api/v1/orders/{id}/cancel`

```
POST /api/v1/orders/{id}/cancel
{ "reason": "customer changed their mind" }
```

**200** → `OrderResponse` (order is now `Cancelled`, stock restored)

Error responses:

| Status | Code | Meaning / UI action |
|---|---|---|
| 409 | `INVALID_ORDER_TRANSITION` | Already cancelled, or the customer may no longer self-cancel (store has started processing/packing). Show as-is; not retryable. |
| 409 | `REFUND_FAILED` | The payment provider rejected the refund. **Nothing was changed** — order still active, stock still reserved. Tell the user to retry later or contact support. |
| 409 | `INVENTORY_RESTORE_FAILED` | Stock could not be returned. **Order not cancelled.** Operational — surface clearly. |
| 409 | `CONCURRENCY_CONFLICT` | Another request changed this order. Refetch and let the user retry. |

### Change order status — `PUT /api/v1/admin/orders/{id}/status`

```
PUT /api/v1/admin/orders/{id}/status
{ "status": "Confirmed", "reason": null, "trackingNumber": null, "trackingProvider": null }
```

Setting `Confirmed` **consumes** the reserved stock (it becomes sold). Setting `Cancelled` routes
through the same refund-then-restore path as the customer cancel above, so all four error codes can
occur here too.

### Verify payment — `POST /api/v1/payments/verify`

If signature verification fails, the order is marked failed and reserved stock is released. Safe to
retry — a duplicate callback will not double-release.

---

## 4. Checkout and pricing

| Method | Route |
|---|---|
| `GET` | `/api/v1/checkout/summary` |
| `POST` | `/api/v1/checkout` |
| `POST` | `/api/v1/payments/verify` |

Stock, coupons, shipping, tax and the COD fee are **always recalculated server-side**. The request
carries no monetary value — never compute a total in the browser and send it.

### Errors that mean "your stock view is stale"

| Code | Status | Meaning |
|---|---|---|
| `INSUFFICIENT_INVENTORY` | 409 | Not enough stock — refetch the summary and show the new availability |
| `PRODUCT_UNAVAILABLE` | 404 | Product/variant no longer available |
| `CART_EMPTY` | 400 | Cart is empty |

On any of these, **refetch `GET /checkout/summary`** rather than showing the stale figures.

---

## 5. Refetch checklist

After a mutation, invalidate/refetch:

| After | Refetch |
|---|---|
| Setting or adjusting stock | Admin product detail + product list + storefront product/featured |
| `POST /checkout` | Checkout summary, product detail, stock badges |
| `POST /orders/{id}/cancel` | Order detail, product stock, storefront availability |
| `PUT /admin/orders/{id}/status` → `Confirmed` | Order detail, product stock, storefront availability |
| `POST /payments/verify` | Order detail, product stock |

---

## 6. Quick reference — all endpoints in this domain

| Method | Route | Auth |
|---|---|---|
| `PUT` | `/api/v1/admin/inventory/{productId}?variantId=` | Admin |
| `POST` | `/api/v1/admin/inventory/{productId}/adjust?variantId=` | Admin |
| `GET` | `/api/v1/admin/products/{id}` | Admin |
| `GET` | `/api/v1/admin/products` | Admin |
| `GET` | `/api/v1/admin/products/{productId}/variants` | Admin || `GET` | `/api/v1/store/products` | Public |
| `GET` | `/api/v1/store/products/{slug}` | Public |
| `GET` | `/api/v1/store/products/{slug}/related` | Public |
| `GET` | `/api/v1/store/featured` | Public |
| `GET` | `/api/v1/checkout/summary` | Customer |
| `POST` | `/api/v1/checkout` | Customer |
| `POST` | `/api/v1/payments/verify` | Customer |
| `GET` | `/api/v1/orders/{id}` | Owner |
| `POST` | `/api/v1/orders/{id}/cancel` | Owner |
| `GET` | `/api/v1/admin/orders/{id}` | Admin |
| `PUT` | `/api/v1/admin/orders/{id}/status` | Admin |

> The wider application exposes 113 endpoints across 23 controllers. Those unrelated to stock
> (auth, cart, promotions, tax, settings, media) are documented in `API-Reference.md`.

---

## 7. Admin UI checklist

### Stock editing
- [ ] Show **available** stock (`available` / `availableStock`), never `onHand`
- [ ] Treat `availableStock: null` as **untracked**, not zero
- [ ] Never show a base row and variant rows together
- [ ] Prefer **Adjust** over absolute **Set** for incremental changes (auditable, conflict-safe)
- [ ] Floor the `onHand` input at `reserved`, not `0`
- [ ] Replace the row from the `InventoryResponse` — never compute the new values optimistically
- [ ] Surface `400` inline on the field; it is a validation error, not retryable

### Orders and availability
- [ ] Disable purchase when `stockAvailability === "OutOfStock"`
- [ ] Handle `409` as retryable where `CONCURRENCY_CONFLICT` / `REFUND_FAILED` / `INVENTORY_RESTORE_FAILED` appear
- [ ] Show "someone else is editing" on `CONCURRENCY_CONFLICT`, not a generic failure
- [ ] Refetch stock after every mutation in §5
- [ ] Never send a client-computed total
