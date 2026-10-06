# Product Attributes & Variants — Admin and Storefront UI Guide

How an operator defines what a product varies on, how the variant rows get created, and how the
storefront turns that into a working selector. Every payload and field name here is taken from the
running code, not from an idealised design.

---

## 1. The mental model

Four ideas, in the order they are configured:

```
  AXIS                 VALUE                    VARIANT                  SELECTION
  ─────                 ─────                    ───────                  ─────────
  "Colour"     ──1:N──▶ "Red"                   ┌──────────────┐         click "Red"
  (attribute)          "Black"          ┌──────▶│ Red  / Small │────1:N──▶ click "Small"
                       "White"          │       │  ₹999  S-001 │         ⇒ match
  "Size"       ──1:N──▶ "Small"         │       └──────────────┘         ⇒ variantId
  (attribute)          "Large"   ───────┘
                                        ┌──────────────┐
                                        │ Red  / Large │  ₹1099  S-002
                                        └──────────────┘
```

| Concept | What it is | Table |
|---|---|---|
| **Attribute** (axis) | One dimension the product varies on: Colour, Size, Storage. Belongs to **one product**. | `product_attributes` |
| **Attribute value** | One selectable choice on an axis: "Red". | `product_attribute_values` |
| **Variant** | One sellable combination — **exactly one value per axis**, plus its own SKU, optional price override, and stock row. | `product_variants` |
| **Selection** | What the customer clicks. Resolves to one `variantId`. | not stored — client-side |

**The single most important consequence:** a variant is identified by its combination of attribute
value ids, and that combination is **unique per product**. The backend rejects duplicates.

### Attributes are per-product, not a global catalogue

`product_attributes` has a `ProductId` FK. "Colour" on product A and "Colour" on product B are
**two unrelated rows with two unrelated sets of values.** There is no global attribute library, and
no cross-product reuse. If you build an admin UI, do not model this as a global "Attributes" page —
it is a section inside each product's editor.

---

## 2. Data model

```
products
  ├─1:N─→ product_attributes        (ProductId, Name, SortOrder)
  │            └─1:N─→ product_attribute_values   (AttributeId, Value, SortOrder)
  ├─1:N─→ product_variants          (ProductId, Sku?, PriceOverride?, SortOrder, IsActive,
  │                                  AttributeValueIds  ← ⚠ comma-separated GUIDs, varchar(1000))
  └─1:N─→ inventory_items           (ProductId, VariantId?, OnHand, Reserved, LowStockThreshold)
```

### ⚠️ The variant → attribute-value link is a CSV string, not a join table

`ProductVariant.AttributeValueIds` is a single nullable `varchar(1000)` holding
`"guid1,guid2"` (`ProductVariant.cs:44`). There is **no foreign key, no index and no referential
integrity**. Consequences you must design around in the UI:

- **Deleting an attribute value does not clean up variants.** `PUT /attributes` deletes values not
  named in the request, and any variant referencing one keeps a dangling GUID. The storefront then
  returns that variant with a **shorter** `attributes` array rather than dropping it, so a stale
  reference can never hide a SKU — but the option silently disappears from its label.
- Nothing prunes those dangling ids. `ProductVariant.ClearAttributeValues()` exists and is never
  called.
- Variant combination uniqueness is enforced **only in application code** (`VariantHandlers.cs:237`)
  by comparing canonical sorted strings. There is no unique index behind it, so it is vulnerable to
  a race between two concurrent creates.

### Money, stock, images — where they live

| Concern | Reality |
|---|---|
| **Price** | `PriceOverride` is an **absolute override**, not a delta. Effective price = `variant.PriceOverride ?? product.Price` (`Product.cs:201`). Setting `0` is valid and means free. |
| **Stock** | Not on the variant. A separate `inventory_items` row per `(ProductId, VariantId)`, **auto-created at 0** when the variant is created. Available = `OnHand − Reserved`. |
| **SKU** | Optional, on both product and variant, each with its own partial unique index. A variant with no SKU **falls back to the product SKU at read time** in cart/checkout/wishlist only — the stored value stays null. |
| **Images** | **Product-level only.** There is no `VariantId` on `product_images` and no image field on the storefront variant response. Selecting "Red" cannot change the picture. |

---

## 3. Admin workflow

Order matters: **attributes must exist before variants can reference them.** All admin endpoints are
`[Authorize(Policy = "AdminOnly")]` and live under `/api/v1/products/{productId}`.

### Step 0 — the product must exist

Attributes and variants hang off a product. Base price and base SKU are set at creation.

### Step 1 — define the axes

One `PUT` **per axis**. The endpoint upserts by **name**, so re-sending the same name edits that
axis rather than creating a duplicate.

```
PUT /api/v1/products/{productId}/attributes
```

```jsonc
{
  "name": "Colour",
  "values": [
    { "value": "Red"   },  // no id  → CREATE
    { "value": "Crimson", "id": "<existing-guid>" },  // id present → RENAME this row
    { "value": "Black" }
  ]
}
```

**The value list is replaced wholesale** (`AttributeHandlers.cs:123-159`):

| In the request | Effect on the stored value list |
|---|---|
| `{ "value": "X" }` with no `id` | New value created |
| `{ "value": "Y", "id": "<guid>" }` | Existing value **renamed** in place, keeping its id |
| Present `id`, different `value` | Rename (this is also how you *edit* a label) |
| Array position | Becomes `SortOrder` — **array order is display order** |
| **Existing value omitted from the array** | **DELETED** — and any variant using it silently loses that axis |

That last row is the dangerous one. An admin UI must warn before dropping a value that variants
reference, because there is no server-side check and no way to recover the mapping.

Response — the **full** attribute list for the product, so the UI can re-render from the response
rather than re-fetching:

```jsonc
{
  "productId": "…",
  "attributes": [
    { "id": "…", "name": "Colour", "sortOrder": 0,
      "values": [ { "id": "v-red", "value": "Red", "sortOrder": 0 },
                  { "id": "v-black", "value": "Black", "sortOrder": 1 } ] }
  ]
}
```

> **UI design note:** to add "Navy" to Colour without risking a delete, you must send **every**
> existing value back with its `id`, plus the new one. A naive "add one value" POST would wipe the
> rest. Read the current state first, append locally, PUT the whole array.

Delete a whole axis with `DELETE /api/v1/products/{productId}/attributes/{attributeId}` (idempotent).

### Step 2 — create the variants

One `POST` per combination.

```
POST /api/v1/products/{productId}/variants
```

```jsonc
{
  "sku": "TSH-RED-S",
  "priceOverride": 999.00,   // absolute; omit or null → inherit product price
  "attributeValueIds": ["v-red", "v-small"]  // exactly ONE value per axis
}
```

- `sortOrder` — **omit it** and the backend appends after the current maximum. This is the
  documented recommended path, so the admin never has to compute ordering.
- `priceOverride: null` → inherits `product.Price`.
- Returns `201` with the full `VariantResponse`.
- **Automatically creates the inventory row at 0 stock**, so `availableStock` is `0`, not `null`.

Server-side validation, all before any write:

| Code | Status | Meaning |
|---|---|---|
| `VARIANT_INVALID_ATTRIBUTE_VALUE` | 400 | An id is not a value on **this** product |
| `VARIANT_DUPLICATE_ATTRIBUTE` | 400 | Two ids came from the **same axis** — one value per axis |
| `VARIANT_DUPLICATE_COMBINATION` | 409 | That combination already exists on this product |
| `VARIANT_SKU_TAKEN` | 409 | SKU already used |

### Step 3 — set stock, per variant

This is the step most often forgotten, and every variant is unsellable until it is done.

```
PUT /api/v1/admin/inventory/{productId}?variantId={variantId}
```
```jsonc
{ "onHand": 25, "lowStockThreshold": 5 }
```

Or a relative change:

```
POST /api/v1/admin/inventory/{productId}/adjust?variantId={variantId}
```
```jsonc
{ "delta": -3, "reason": "damaged in transit" }
```

> **⚠️ Set stock on the VARIANT, not the product, once variants exist.** A product may have either a
> base inventory row (`variantId` null) **or** per-variant rows, never both — a database trigger
> (`ck_inventory_row_kind_consistency`) rejects the combination. The API returns a friendly
> `400` if you try to create a base row while variants exist, but note that creating the *first*
> variant on an already stock-tracked product can trip the trigger as an unhandled database error.
> Practical rule: if a product is going to have variants, set the base stock to 0 or skip it, then
> add variants, then set per-variant stock.

### Step 4 — verify

```
GET /api/v1/products/{productId}/attributes   → axes and values with ids
GET /api/v1/products/{productId}/variants     → variants + resolved labels + availableStock
```

---

## 4. Admin UI — recommended screens

### Screen A — Attributes editor (inside the product form)

```
  Attributes                                    [ + Add attribute ]

  ┌────────────────────────────────────────────────────────────┐
  │ Colour                                       Sort ▲  Delete│
  │  ┌────────────────────────────────────────────────────────┐  │
  │  │ Red      [×]   ← drag to reorder                       │  │
  │  │ Black    [×]                                        │  │
  │  │ White    [×]                                        │  │
  │  └────────────────────────────────────────────────────────┘  │
  │  [+ Add value]                          3 values           │
  └────────────────────────────────────────────────────────────┘

  ┌────────────────────────────────────────────────────────────┐
  │ Size                                          Sort ▲  Delete│
  │  Small [×]   Medium [×]   Large [×]                        │
  └────────────────────────────────────────────────────────────┘
```

Save behaviour: keep local state, and on save send **one `PUT` per axis** with the complete array —
existing values carrying their `id`, new ones without. Dropping a value that variants reference must
show a confirmation naming the affected variant count.

### Screen B — Variant matrix (the highest-value screen)

Because combinations are mechanical (`Colour × Size` = N×M rows), generate them instead of making an
operator click one per row. This is the screen that removes essentially all the tedium:

```
  Variants                                       [ ⚡ Generate combinations ]

  ┌────────────┬─────────┬──────────┬────────┬──────────────┬────────┐
  │ Colour     │ Size    │ Price    │ SKU    │ Stock        │ State  │
  ├────────────┼─────────┼──────────┼────────┼──────────────┼────────┤
  │ ● Red      │ Small   │ 999.00   │[auto]  │ [ 25 ]       │ Active │
  │ ● Red      │ Medium  │ 999.00   │[auto]  │ [ 10 ]       │ Active │
  │ ○ Black    │ Small   │ 999.00   │[auto]  │ [  0 ]       │ Active │
  │ ...        │         │          │        │              │        │
  └────────────┴─────────┴──────────┴────────┴──────────────┴────────┘
  6 combinations · 2 sold out                            [Save all]
```

The Generate button takes the cartesian product of every axis and diffs it against existing
variants, creating only the missing ones:

```ts
// client-side only — the backend has no batch endpoint
function* combinations<T>(axes: T[][]): Generator<T[]> {
  if (axes.length === 0) { yield []; return; }
  const [head, ...rest] = axes;
  for (const h of head) for (const tail of combinations(rest)) yield [h, ...tail];
}

const existing = new Set(variants.map(v => canonical(v.attributeValueIds)));
const missing  = [...combinations(attributeAxes)].filter(c => !existing.has(canonical(c.map(i => i.id))));
// POST one variant per missing combination
```

Also offer an "incomplete combinations" warning — the backend permits a variant that omits an axis
(see §7), so `{Red}` with no Size is storable and will confuse the storefront selector.

### Screen C — SKU handling

Leave SKU blank and it is stored as null, and the cart falls back to the product SKU. Auto-generating
`{PRODUCT-SKU}-{RED}-{S}` is friendlier for operators, but the uniqueness check is **per table**, so
a generated SKU colliding with a product SKU will not be caught. Treat SKU as advisory.

---

## 5. Storefront — what the API gives you

A single public call returns everything needed to render the selector:

```
GET /api/v1/store/products/{slug}
```

```jsonc
{
  "id": "…", "name": "Classic T-Shirt", "price": 899.00,
  "images": [ { "secureUrl": "…", "isPrimary": true, "sortOrder": 0 } ],

  // the axes — build the selector rows from this
  "attributes": [
    { "id": "a-colour", "name": "Colour", "sortOrder": 0,
      "values": [ { "id": "v-red", "value": "Red", "sortOrder": 0 },
                  { "id": "v-black", "value": "Black", "sortOrder": 1 } ] },
    { "id": "a-size", "name": "Size", "sortOrder": 1,
      "values": [ { "id": "v-small", "value": "Small", "sortOrder": 0 },
                  { "id": "v-large", "value": "Large", "sortOrder": 1 } ] }
  ],

  // the combinations — each already carries its resolved price and availability
  "variants": [
    { "id": "var-1", "sku": "TSH-RED-S",  "effectivePrice": 999.00, "sortOrder": 0,
      "isActive": true, "stockAvailability": "InStock", "canPurchase": true, "isOutOfStock": false,
      "attributeValueIds": "v-red,v-small",
      "attributes": [ { "attributeValueId": "v-red",   "attributeId": "a-colour",
                        "attributeName": "Colour", "value": "Red" },
                      { "attributeValueId": "v-small", "attributeId": "a-size",
                        "attributeName": "Size",   "value": "Small" } ] },
    { "id": "var-4", "effectivePrice": 899.00, "canPurchase": false,
      "stockAvailability": "OutOfStock", "isOutOfStock": true,
      "attributeValueIds": "v-black,v-large", "attributes": [ … ] }
  ]
}
```

Two things to note:

- `effectivePrice` is already resolved — **never show `product.price` next to a selected variant.**
- `canPurchase` is `isActive && inStock`. An inactive variant is `canPurchase: false` regardless of
  stock, so the UI can disable it without re-deriving the rule.
- `stockAvailability` is a band (`InStock` / `LowStock` / `OutOfStock`), never a unit count. Exact
  counts are deliberately admin-only.
- A product with **no** variants returns `variants: []`, and the product's own `price` /
  `stockAvailability` / `canPurchase` describe the whole product. Your UI must branch on
  `variants.length === 0` and hide the selector entirely.

> **⚠️ The product's own `stockAvailability` / `isOutOfStock` is a ROLLUP, not the selection.**
> For a product with variants it reports out-of-stock only when *every* sellable variant is out of
> stock — one purchasable variant makes the whole product read as "in stock"
> (`StorefrontProductResponse.cs:63-68`). This is exactly why the button must gate on
> `variant.canPurchase`: the product badge will happily say "In stock" while the chosen Red/Large
> combination is sold out.

---

## 6. Storefront — the matching logic you must write yourself

**This is the part that does not exist in the backend.** There is no "resolve these selected values
to a variant" endpoint and no server-side combination logic. `StorefrontProductsController` exposes
only list, by-slug, related and featured routes. Resolution is entirely the client's job — confirmed
in `API-Reference.md:535-538`.

```ts
type AttributeValue = { id: string; value: string; sortOrder: number };
type Attribute      = { id: string; name: string; sortOrder: number; values: AttributeValue[] };
type Variant        = {
  id: string; effectivePrice: number; isActive: boolean;
  stockAvailability: 'InStock' | 'LowStock' | 'OutOfStock';
  canPurchase: boolean;
  attributeValueIds: string | null;
};

const parseIds = (csv: string | null): string[] =>
  (csv ?? '').split(',').map(s => s.trim()).filter(Boolean);

/** The variant matching a complete selection — exact set equality, order-independent. */
function findVariant(variants: Variant[], selected: Record<string, string>): Variant | undefined {
  const wanted = Object.values(selected).sort();
  return variants.find(v => {
    const ids = parseIds(v.attributeValueIds).sort();
    return ids.length === wanted.length && ids.every((id, i) => id === wanted[i]);
  });
}

/**
 * Is `valueId` still reachable given the rest of the current selection?
 * Drives greyed-out swatches. An option is available if some variant contains it
 * AND, for every *other* axis already chosen, that variant agrees with the choice.
 */
function isValueAvailable(
  variants: Variant[], selected: Record<string, string>, valueId: string,
): boolean {
  return variants.some(v => {
    const ids = parseIds(v.attributeValueIds);
    if (!ids.includes(valueId) || !v.canPurchase) return false;
    return Object.entries(selected)
      .filter(([axis, id]) => id !== valueId)
      .every(([, id]) => ids.includes(id));
  });
}
```

Render and gate like this:

```ts
const [selection, setSelection] = useState<Record<string, string>>({});
const variant = findVariant(product.variants, selection);
const ready = product.variants.length === 0
  ? product.canPurchase
  : Object.keys(selection).length === product.attributes.length && variant?.canPurchase === true;
```

```
  Colour        Size          Price
  ● Red         S  M  L       ₹999.00        ← ready: canPurchase true
  ○ Black       S  M  L                    ← ready: canPurchase false
                ↑ greyed
```

Two details that matter in practice:

- **Gate the button on `variant.canPurchase`, not on the product's.** A product can be in stock
  overall while the chosen combination is sold out.
- **Re-fetch after a failed add-to-cart.** `canPurchase` is a snapshot; stock moves. The inventory
  row is the authority.

Then the purchase call carries the resolved id:

```jsonc
// POST /api/v1/cart/items
{ "productId": "…", "variantId": "var-1", "quantity": 1 }
```

There is no attribute-value input on cart add. The server verifies the variant exists, belongs to
the product and is active — it never re-derives the variant from the selection, so **the client is
trusted to send the right id.**

---

## 7. Gaps to design around

Ordered by how likely they are to bite.

**1. No server-side variant resolver.** If your frontend cannot compute the combination, there is no
API to call. This is the single largest piece of missing functionality, and §6 is the whole of it.

**2. Variant images are implemented.** Selecting "Red" can now change the photo. Variant images
are uploaded via `POST /api/v1/products/{productId}/variants/{variantId}/images` and returned in
the grid and PDP responses. See `docs/41-Variant-Grid-and-Selection-UI-Guide.md` §7 for the
frontend fallback chain.

**3. No completeness check.** Nothing requires a variant to specify a value for every axis the
product declares. A product with `{Colour, Size}` can hold a variant `{Red}` with no size. Two
variants `{Red}` and `{Red, Large}` both resolve, and exact set-equality matching in §6 will never
select the `{Red}` one once a size is chosen — it becomes unreachable stock. **Flag incomplete
combinations in the admin matrix** and consider treating them as configuration errors.

**4. Deleting an attribute value orphans variants.** Wholesale replacement (§Step 1) deletes values
with no reference check. The variant survives with a shorter `attributes` array, so its label
silently loses a dimension. Warn in the admin UI; the backend will not.

**5. `PriceOverride` is absolute, so base price edits don't propagate.** Change `product.price`
from 899 to 950 and every variant with an override stays frozen at its override. A UI that
"updates all variant prices by X%" is not expressible. Either leave overrides null to inherit, or
show a clear per-variant price column and accept that they are independent.

**6. A missing inventory row reads as purchasable.** `StorefrontStockService` returns
`InStock, CanPurchase = true` for a null inventory row. In practice every variant created through
the API gets a row at 0, so this mostly shows up for variants inserted by hand — but a 0-stock
variant is `OutOfStock`, whereas an untracked one is freely sellable.

**7. SKU uniqueness is per table.** The same literal can exist as both a product SKU and a variant
SKU. Order lines and invoices surface SKUs, so a collision makes reporting ambiguous.

---

## 8. Complete worked example

Product: *Classic T-Shirt*, base price ₹899.

```jsonc
// 1. axis 1 — Colour
PUT /api/v1/products/P1/attributes
{ "name": "Colour",
  "values": [ {"value":"Red"}, {"value":"Black"}, {"value":"White"} ] }

// 2. axis 2 — Size
PUT /api/v1/products/P1/attributes
{ "name": "Size",
  "values": [ {"value":"Small"}, {"value":"Medium"}, {"value":"Large"} ] }

// 3. read back to capture the generated ids
GET /api/v1/products/P1/attributes
// → v-red, v-black, v-white, v-small, v-medium, v-large

// 4. one variant per combination (generated from the matrix in Screen B)
POST /api/v1/products/P1/variants
{ "sku": "TSH-RED-S",    "priceOverride": 999.00, "attributeValueIds": ["v-red","v-small"] }
POST /api/v1/products/P1/variants
{ "sku": "TSH-RED-M",   "attributeValueIds": ["v-red","v-medium"] }      // no override → 899
POST /api/v1/products/P1/variants
{ "sku": "TSH-BLK-L",   "attributeValueIds": ["v-black","v-large"] }

// 5. stock, per variant — required, or they are unsellable
PUT /api/v1/admin/inventory/P1?variantId=VAR1   { "onHand": 25 }
PUT /api/v1/admin/inventory/P1?variantId=VAR2   { "onHand": 10 }
PUT /api/v1/admin/inventory/P1?variantId=VAR3   { "onHand": 0  }   // shows OutOfStock

// 6. storefront reads it all from one call
GET /api/v1/store/products/classic-t-shirt
```

Note `TSH-RED-M` with no `priceOverride` returns `effectivePrice: 899` — it inherits the product
price, and will track future base-price edits, unlike the overridden variants.

---

## 9. Error reference

| Code | Status | Where | Meaning / UI response |
|---|---|---|---|
| `PRODUCT_NOT_FOUND` | 404 | attributes, variants, inventory | Wrong product id |
| `VARIANT_NOT_FOUND` | 404 | variant get/update/delete | — |
| `VARIANT_INVALID_ATTRIBUTE_VALUE` | 400 | create/update variant | A value id belongs to another product. Reload attributes |
| `VARIANT_DUPLICATE_ATTRIBUTE` | 400 | create/update variant | Two values from the same axis. One per axis |
| `VARIANT_DUPLICATE_COMBINATION` | 409 | create/update variant | Combination exists. Switch the row to update instead |
| `VARIANT_SKU_TAKEN` | 409 | create/update variant | SKU in use **within `product_variants`** |
| *(inventory)* | 400 | set stock with `variantId` omitted | Variants exist; set stock per variant instead |

---

## 10. Where this lives in code

| Concern | File |
|---|---|
| Attribute + value entities | `src\KromicCommerce.Domain\Catalog\ProductAttribute.cs` |
| Variant entity, CSV parsing | `src\KromicCommerce.Domain\Catalog\ProductVariant.cs` |
| **Effective-price rule** | `src\KromicCommerce.Domain\Catalog\Product.cs:201` |
| Inventory entity | `src\KromicCommerce.Domain\Catalog\InventoryItem.cs` |
| Attribute upsert / delete | `src\KromicCommerce.Application\Features\Catalog\Products\Attributes\AttributeHandlers.cs` |
| Variant create/update, validation, duplicate check | `.../Products\Variants\VariantHandlers.cs` |
| Variant label resolution | `.../Products\Variants\VariantHandlers.cs:283` (`VariantAttributeHelper.ResolveAsync`) |
| Admin endpoints | `src\KromicCommerce.Api\Controllers\V1\ProductsController.cs:125-271` |
| Stock endpoints | `src\KromicCommerce.Api\Controllers\V1\InventoryController.cs` |
| Storefront projection | `.../Storefront\Products\GetStorefrontProductBySlug\GetStorefrontProductBySlugHandler.cs:118-152` |
| Stock → availability bands | `src\KromicCommerce.Application\Services\StorefrontStockService.cs` |
| Inventory row-kind trigger | `src\KromicCommerce.Infrastructure\Persistence\Migrations\20261002130000_InventoryRowKindConsistency.cs` |
| Selector contract (original) | `docs\Inventory-Frontend-Integration-Guide.md:399-411` |
| Variant grid endpoint | `src\KromicCommerce.Application\Features\Storefront\Products\GetStorefrontVariantGrid\GetStorefrontVariantGridHandler.cs` |
| Variant image CRUD | `src\KromicCommerce.Api\Controllers\V1\VariantImagesController.cs` |

---

## See also

- **`docs/41-Variant-Grid-and-Selection-UI-Guide.md`** — the source of truth for frontend
  implementation: variant grid rendering, PDP selector state machine, add-to-cart rules, admin
  variant image management, complete TypeScript types, and frontend checklist.
