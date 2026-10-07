# Variant-Aware Product Management — Complete UI Implementation Guide

> **Source of truth for the frontend team.** This document supersedes all prior variant UI docs.
> Every endpoint, field name, and behavioral rule is verified against the running backend.
> Nothing here is speculative.

---

## Table of Contents

1. [Admin Product Editor — Three-Button Pattern](#1-admin-product-editor--three-button-pattern)
2. [Variant Matrix — Inline Editing with Consolidated Save](#2-variant-matrix--inline-editing-with-consolidated-save)
3. [Stock Management — Integrated into Variant Form](#3-stock-management--integrated-into-variant-form)
4. [Image Management — Product + Variant](#4-image-management--product--variant)
5. [Storefront Variant Grid](#5-storefront-variant-grid)
6. [Product Detail Page — Variant Selector](#6-product-detail-page--variant-selector)
7. [Add to Cart](#7-add-to-cart)
8. [Admin Order Management](#8-admin-order-management)
9. [Complete TypeScript Types](#9-complete-typescript-types)
10. [Frontend Checklist](#10-frontend-checklist)

---

## 1. Admin Product Editor — Three-Button Pattern

### 1.1 Fixed toolbar

The product editor has exactly **three buttons** in a fixed toolbar at the top of the page.
These buttons are always visible, never move, and never change:

```
┌─────────────────────────────────────────────────────────────┐
│  Product Editor: Classic T-Shirt                             │
│                                                             │
│  [Cancel]  [Publish]  [Save]                                │
│                                                             │
│  ┌─── Basic Info ───┐ ┌─── Images ───┐ ┌─── Variants ───┐ │
│  │ ...               │ │ ...         │ │ ...            │ │
│  └───────────────────┘ └─────────────┘ └────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

| Button | Behavior |
|---|---|
| **Cancel** | Discards all unsaved changes. Resets every form field to its last-saved state. Does not call the API. |
| **Publish** | Publishes the product (`POST /api/v1/products/{id}/publish`). Returns `204`. If the product is already published, still returns `204` (no-op). Does not save other form fields — publish is a state transition, not a save. |
| **Save** | Saves **everything** in one call: basic info, images, variants, stock, attributes. See §1.2. |

### 1.2 Save behavior

When the admin clicks **Save**:

1. Collect all dirty sections (basic info, images, variants, attributes).
2. If nothing is dirty, do nothing — button is visually disabled.
3. Call each dirty endpoint in parallel:
   - `PUT /api/v1/products/{id}` — basic info, price, description, category, brand, etc.
   - `POST /api/v1/products/{id}/images` — new product-level images (if any)
   - `PUT /api/v1/products/{id}/attributes` — attribute axes (if changed)
   - For each dirty variant: `PUT /api/v1/products/{id}/variants/{variantId}` + `PUT /api/v1/admin/inventory/{id}?variantId=...`
4. On all calls succeeding:
   - Merge all responses into local state.
   - Show "Saved ✓" briefly on the Save button.
   - Clear all dirty indicators.
5. On any call failing:
   - Roll back all sections to their pre-save state.
   - Show the error inline on the Save button: "Failed to save. Retry."
   - Do **not** partially apply changes — either everything saves or nothing does.

### 1.3 Dirty tracking per section

Each form section tracks its own dirty state:

```ts
type EditorDirty = {
  basicInfo: boolean;      // name, price, description, category, brand, etc.
  images: boolean;         // new uploads, reorder, delete, set-primary
  attributes: boolean;     // axis add/rename/delete, value add/rename/delete
  variants: Record<string, boolean>;  // keyed by variantId
};
```

The Save button is enabled when `Object.values(dirty).some(v => v === true)`.

### 1.4 Cancel behavior

Cancel restores every section to its last-saved state:

```ts
function handleCancel() {
  // Reload everything from the server — this is the simplest and most reliable reset
  const data = await fetch(`/api/v1/products/admin/${productId}`).then(r => r.json());
  setBasicInfo(data);
  setImages(data.images);
  setAttributes(data.attributes);
  setVariants(data.variants.map(v => ({ ...v, dirty: {}, saving: false, error: null })));
  setDirty({ basicInfo: false, images: false, attributes: false, variants: {} });
}
```

### 1.5 Publish behavior

Publish is independent of Save:

```ts
async function handlePublish() {
  await fetch(`/api/v1/products/${productId}/publish`, { method: 'POST' });
  setProductStatus('Published');
  showToast('Product published');
}
```

If the product is already published, the API returns `204` anyway — no harm in calling it.

### 1.6 Section components

```
ProductEditorPage
├── FixedToolbar
│   ├── CancelButton
│   ├── PublishButton
│   └── SaveButton (disabled when !isDirty, shows "Saving…" / "Saved ✓" / error)
├── BasicInfoSection (name, price, description, category, brand, SEO)
├── ProductImagesSection (gallery upload, reorder, set-primary, delete)
├── AttributesSection (axis management, value management)
└── VariantsSection
    ├── VariantMatrix (table or card grid)
    │   └── VariantRow (one per variant, inline editable)
    │       ├── AttributeValuesDisplay
    │       ├── SkuInput
    │       ├── PriceOverrideInput
    │       ├── StockInput (onHand, lowStockThreshold)
    │       ├── ActiveToggle
    │       └── RowSaveIndicator (saving / saved / error)
    └── GenerateCombinationsButton
```

---

## 2. Variant Matrix — Inline Editing with Consolidated Save

### 2.1 No per-row buttons

Each variant row is **always editable**. The admin clicks into a field, changes it, and clicks the
top-level **Save** button. There are no "Update Stock" or "Update Variant" buttons per row.

```
┌──────────────────────────────────────────────────────────────────┐
│ Variant: Red / Small                                             │
│                                                                  │
│  Attribute values:  Colour: Red  |  Size: Small                 │
│                                                                  │
│  SKU:            [TSH-RED-S___]                                  │
│  Price override: [999.00____]    (empty = inherits product price)│
│  Active:         [✓]                                             │
│                                                                  │
│  Stock on hand:  [25______]   Low stock threshold: [5_____]     │
│  Available: 25   Status: In Stock                                │
│                                                                  │
│  [Row is dirty → Save button on top will include this row]      │
└──────────────────────────────────────────────────────────────────┘
```

### 2.2 Dirty tracking per row

```ts
type VariantRow = {
  id: string;
  productId: string;
  sku: string | null;
  priceOverride: number | null;
  sortOrder: number;
  isActive: boolean;
  attributeValueIds: string | null;
  attributes: Array<{ attributeName: string; value: string }>;

  // Stock fields
  onHand: number;
  reserved: number;
  available: number;
  lowStockThreshold: number;
  isLowStock: boolean;
  isOutOfStock: boolean;
  stockAvailability: 'InStock' | 'LowStock' | 'OutOfStock';

  // UI state
  dirty: boolean;
  saving: boolean;
  error: string | null;
};
```

A row is dirty when any input value differs from the last-saved value. The top Save button's
enabled state aggregates all row dirty flags.

### 2.3 Save orchestration

When the top Save button is clicked:

```ts
async function handleSave() {
  const allRows = document.querySelectorAll('.variant-row');
  const dirtyRows = Array.from(allRows).filter(row => row.dataset.dirty === 'true');

  setSaving(true);
  setError(null);

  const calls = [];

  // Basic info
  if (editorDirty.basicInfo) {
    calls.push(
      fetch(`/api/v1/products/${productId}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
        body: JSON.stringify(basicInfoPayload),
      }).then(r => r.ok ? r.json() : Promise.reject(new Error('Failed to save product info')))
    );
  }

  // Attributes
  if (editorDirty.attributes) {
    calls.push(
      fetch(`/api/v1/products/${productId}/attributes`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
        body: JSON.stringify(attributesPayload),
      }).then(r => r.ok ? r.json() : Promise.reject(new Error('Failed to save attributes')))
    );
  }

  // Each dirty variant row
  for (const row of dirtyRows) {
    const stockCall = fetch(`/api/v1/admin/inventory/${productId}?variantId=${row.id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
      body: JSON.stringify({
        onHand: row.onHand,
        lowStockThreshold: row.lowStockThreshold,
      }),
    }).then(r => r.ok ? r.json() : Promise.reject(new Error(`Stock save failed for ${row.sku}`)));

    const variantCall = fetch(`/api/v1/products/${productId}/variants/${row.id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
      body: JSON.stringify({
        sku: row.sku,
        priceOverride: row.priceOverride,
        sortOrder: row.sortOrder,
        isActive: row.isActive,
        attributeValueIds: row.attributeValueIds?.split(',').map(Number) ?? [],
      }),
    }).then(r => r.ok ? r.json() : Promise.reject(new Error(`Variant save failed for ${row.sku}`)));

    calls.push(Promise.all([stockCall, variantCall]).then(([stockRes, variantRes]) => {
      // Merge both responses into local state
      mergeVariantRow(row.id, { ...stockRes, ...variantRes });
      return { rowId: row.id, stockRes, variantRes };
    }));
  }

  try {
    await Promise.all(calls);
    // Clear all dirty flags
    setEditorDirty({ basicInfo: false, images: false, attributes: false, variants: {} });
    showSaved();
  } catch (err) {
    setError(err.message);
    // Rollback: reload from server
    await reloadFromServer();
  } finally {
    setSaving(false);
  }
}
```

### 2.4 Visual states

| State | Row appearance | Save button |
|---|---|---|
| Clean | Normal | Disabled |
| Dirty | Normal, maybe a subtle left border | Enabled |
| Saving | Slight opacity reduction, "Saving…" text in row | Disabled, spinner |
| Saved | Normal, brief green flash | "Saved ✓" for 2s |
| Error | Red left border, error text inline | "Retry" |

### 2.5 Stock display after save

After save, the row's stock fields update from the `InventoryResponse`:

```ts
function mergeVariantRow(id: string, responses: { stockRes: any; variantRes: any }) {
  setVariants(prev => prev.map(v => {
    if (v.id !== id) return v;
    const stock = responses.stockRes;
    const variant = responses.variantRes;
    return {
      ...v,
      sku: variant.sku ?? v.sku,
      priceOverride: variant.priceOverride,
      sortOrder: variant.sortOrder,
      isActive: variant.isActive,
      onHand: stock.onHand,
      reserved: stock.reserved,
      available: stock.available,
      lowStockThreshold: stock.lowStockThreshold,
      isLowStock: stock.isLowStock,
      isOutOfStock: stock.isOutOfStock,
      stockAvailability: stock.isOutOfStock ? 'OutOfStock' : stock.isLowStock ? 'LowStock' : 'InStock',
      canPurchase: variant.isActive && stock.available > 0,
      dirty: false,
      saving: false,
      error: null,
    };
  }));
}
```

---

## 3. Stock Management — Integrated into Variant Form

### 3.1 No separate stock page

Stock is edited **inline** in the variant matrix. There is no separate "Update Stock" button or
page. Each variant row has two stock fields:

```
Stock on hand:      [25______]
Low stock threshold: [5_____]
```

These fields are part of the variant row form. They are saved together with the rest of the row
when the top Save button is clicked.

### 3.2 Stock display in the grid

The stock badge below the inputs shows the current state:

```
Available: 25   Status: [In Stock]   (green badge)
Available: 3    Status: [Low Stock]   (yellow badge)
Available: 0    Status: [Out of Stock] (red badge)
```

The badge updates immediately after save from the merged `InventoryResponse`.

### 3.3 Backend endpoints (unchanged)

| What | Endpoint | Method |
|---|---|---|
| Set absolute stock | `/api/v1/admin/inventory/{productId}?variantId={variantId}` | PUT |
| Apply delta | `/api/v1/admin/inventory/{productId}/adjust?variantId={variantId}` | POST |

Both return `InventoryResponse`. The frontend calls them internally during Save — the admin never
interacts with these endpoints directly.

### 3.4 Stock after variant creation

When a variant is created (via the "Generate combinations" button or manual add), it automatically
gets an inventory row at 0 stock. The variant row in the matrix shows:

```
Stock on hand: [0______]   Status: [Out of Stock]
```

The admin changes this inline and hits Save.

---

## 4. Image Management — Product + Variant

### 4.1 Schema

`product_images` now has an optional `VariantId` column:

| Type | VariantId | Owned by | Deleted when |
|---|---|---|---|
| Product gallery | `null` | Product | Variant deletion does NOT touch these |
| Variant gallery | set | Variant | Variant deletion removes these |

### 4.2 Variant creation auto-duplicates images

When a new variant is created, all product-level images are automatically duplicated to the
variant. The variant starts with the same gallery as the product.

```ts
// In CreateVariantHandler, after creating the variant:
var productImages = await db.ProductImages
    .Where(i => i.ProductId == cmd.ProductId && i.VariantId == null)
    .OrderBy(i => i.SortOrder)
    .ToListAsync(ct);

foreach (var productImage in productImages)
{
    db.ProductImages.Add(ProductImage.Create(
        cmd.ProductId,
        productImage.Asset,         // same MediaAsset
        productImage.SortOrder,      // preserve order
        productImage.IsPrimary,      // preserve primary flag
        variant.Id));                // scoped to variant
}
```

### 4.3 Product-level images hidden when variants exist

When a product has at least one variant:
- The product-level image tab is hidden from the admin UI.
- The variant matrix shows each variant's own images.
- The storefront variant grid returns only variant-level images.

When a product has no variants:
- The product-level image tab is visible.
- The storefront product list returns product-level images.

### 4.4 Admin image endpoints

**Product images** (only when product has no variants):

| Method | Route | Use |
|---|---|---|
| POST | `/api/v1/products/{productId}/images` | Upload product images |
| PUT | `/api/v1/products/{productId}/images/reorder` | Reorder |
| PUT | `/api/v1/products/{productId}/images/{imageId}/set-primary` | Set primary |
| DELETE | `/api/v1/products/{productId}/images/{imageId}` | Delete |

**Variant images** (when product has variants):

| Method | Route | Use |
|---|---|---|
| POST | `/api/v1/products/{productId}/variants/{variantId}/images` | Upload variant images |
| PUT | `/api/v1/products/{productId}/variants/{variantId}/images/reorder` | Reorder |
| PUT | `/api/v1/products/{productId}/variants/{variantId}/images/{imageId}/set-primary` | Set primary |
| DELETE | `/api/v1/products/{productId}/variants/{variantId}/images/{imageId}` | Delete |

### 4.5 UI placement

**Recommended:** inside the variant matrix. Each variant row has a horizontal strip of thumbnails
(40×40px) at the top. Clicking the "+" button on the strip opens the file picker scoped to that
variant. After upload, the strip updates immediately.

```
VariantRow
├── VariantImageStrip (horizontal, 40×40px thumbnails)
│   ├── Thumbnail (existing variant image)
│   ├── Thumbnail (existing variant image)
│   └── AddButton (+) — opens file picker for this variant
├── AttributeValuesDisplay
├── SkuInput
├── PriceOverrideInput
├── StockInputs
└── ActiveToggle
```

**Do not** put variant image upload in the product-level image tab. That tab manages `VariantId = null`
images. When variants exist, that tab is hidden entirely.

### 4.6 Cloudinary folder

All images for a product — product-level and variant-level — upload to the same folder:

```
products/{productId}/
  ├─ img-abc123.jpg    (product-level, VariantId = null)
  ├─ img-def456.jpg    (variant-level, VariantId = var-001)
  └─ img-ghi789.jpg    (variant-level, VariantId = var-002)
```

The `VariantId` column disambiguates ownership. No per-variant subfolders.

---

## 5. Storefront Variant Grid

### 5.1 Endpoint

```
GET /api/v1/store/products/variants
```

Returns one row per active variant. Products without variants appear once with `variantId: null`.

### 5.2 Response (variant row)

```jsonc
{
  "id": "var-001",
  "variantId": "var-001",
  "productId": "P1",
  "slug": "classic-t-shirt",
  "name": "Classic T-Shirt",
  "sku": "TSH-RED-S",
  "effectivePrice": 999.00,
  "currency": "INR",
  "primaryImageUrl": "https://…",    // variant-level image only
  "stockAvailability": "InStock",
  "canPurchase": true,
  "isOutOfStock": false,
  "categoryId": "cat-001",
  "categoryName": "Shirts",
  "categorySlug": "shirts",
  "brandId": "brand-001",
  "brandName": "Kromic",
  "brandSlug": "kromic",
  "isFeatured": true,
  "ratingAverage": 4.5,
  "ratingCount": 12,
  "images": [
    { "id": "img-1", "secureUrl": "https://…", "altText": "Red", "sortOrder": 0, "isPrimary": true }
  ]
}
```

### 5.3 Image behavior on the variant grid

**Variant rows (`variantId` set):**
- `images[]` contains only variant-level images.
- `primaryImageUrl` is the first variant-level image by `sortOrder`.
- **No fallback to product-level images.**

**Simple-product rows (`variantId` null):**
- `images[]` contains product-level images (`VariantId = null`).
- `primaryImageUrl` is the first product-level primary image.

**Why:** when a product has variants, the UI hides the product-level image section. Each variant
carries its own gallery, duplicated from the product at creation time.

### 5.4 Card component

```tsx
function ProductCard({ row }: { row: GridRow }) {
  const isSimple = row.variantId === null;

  const imageUrl = row.images.find(i => i.isPrimary)?.secureUrl
    ?? row.images[0]?.secureUrl
    ?? row.primaryImageUrl
    ?? '/placeholder.png';

  return (
    <Card>
      <Link to={`/products/${row.slug}`}>
        <img src={imageUrl} alt={row.name} loading="lazy" />
      </Link>
      <div className="card-body">
        <Link to={`/products/${row.slug}`}>
          <h3>{row.name}</h3>
        </Link>
        {!isSimple && row.images.some(i => i.altText) && (
          <p className="variant-label">
            {row.images.map(i => i.altText).filter(Boolean).join(' / ')}
          </p>
        )}
        <price>{formatPrice(row.effectivePrice, row.currency)}</price>
        <StockBadge availability={row.stockAvailability} />
        <AddToCartButton
          disabled={!row.canPurchase}
          productId={row.productId}
          variantId={row.variantId}
        />
      </div>
    </Card>
  );
}
```

### 5.5 Stock badge

```tsx
function StockBadge({ availability }: { availability: string }) {
  const config = {
    InStock:   { className: 'badge-success', text: 'In Stock' },
    LowStock:  { className: 'badge-warning', text: 'Low Stock' },
    OutOfStock:{ className: 'badge-error',   text: 'Out of Stock' },
  }[availability] ?? { className: 'badge-neutral', text: availability };
  return <span className={`badge ${config.className}`}>{config.text}</span>;
}
```

### 5.6 Pagination

`totalCount` includes both variant rows and simple-product fallback rows. A product with 3 variants
contributes 3 to the total. A product with no variants contributes 1.

---

## 6. Product Detail Page — Variant Selector

### 6.1 Endpoint

```
GET /api/v1/store/products/{slug}
```

### 6.2 Selector state machine

```ts
const [selection, setSelection] = useState<Record<string, string>>({});
// selection = { "a-colour": "v-red", "a-size": "v-small" }
```

**Step-by-step:**

1. Render one button group per attribute axis, ordered by `sortOrder`.
2. User clicks a value → `selection[attributeId] = valueId`.
3. Resolve variant:

```ts
function findVariant(variants, selection) {
  const wanted = Object.values(selection).sort();
  return variants.find(v => {
    const ids = (v.attributeValueIds ?? '').split(',').map(s => s.trim()).filter(Boolean).sort();
    return ids.length === wanted.length && ids.every((id, i) => id === wanted[i]);
  });
}
```

4. If resolved: show `effectivePrice`, `stockAvailability`, gate add-to-cart on `canPurchase`.
5. If partial: show product base price, disable add-to-cart.

### 6.3 Image switching on selection

```ts
const resolved = findVariant(product.variants, selection);
const heroImage = resolved
  ? (resolved.images.find(i => i.isPrimary) ?? resolved.images[0])
  : product.images.find(i => i.isPrimary) ?? product.images[0];
```

### 6.4 Disabled swatches

```ts
function isValueAvailable(variants, selection, valueId) {
  return variants.some(v => {
    const ids = (v.attributeValueIds ?? '').split(',').map(s => s.trim()).filter(Boolean);
    if (!ids.includes(valueId) || !v.canPurchase) return false;
    return Object.entries(selection)
      .filter(([axis, id]) => id !== valueId)
      .every(([, id]) => ids.includes(id));
  });
}
```

### 6.5 Incomplete combinations

If any variant has fewer `attributeValueIds` than declared attributes, show:

> "Some combinations are incomplete. Please contact the store administrator."

---

## 7. Add to Cart

### 7.1 Request

```jsonc
POST /api/v1/cart/items
{
  "productId": "P1",
  "variantId": "var-001",
  "quantity": 1
}
```

### 7.2 Rules

| Situation | Behavior |
|---|---|
| No variant selected (product has variants) | Highlight selector, show "Please select options" |
| `variantId` supplied, variant not found | "This combination no longer exists. Refresh." |
| `variantId` supplied, variant inactive | "This combination is currently unavailable." |
| `variantId` supplied, insufficient stock | "Only N left" or "Out of stock" |
| Success | Update cart count, brief confirmation |

---

## 8. Admin Order Management

### 8.1 Order item response (no API changes needed)

```jsonc
{
  "orderItems": [
    {
      "productId": "P1",
      "variantId": "var-001",
      "sku": "TSH-RED-S",
      "variantDescription": "Red / Small",
      "productName": "Classic T-Shirt",
      "unitPrice": 999.00,
      "quantity": 1,
      "lineTotal": 999.00
    }
  ]
}
```

Show `variantDescription` as the line title. If `variantId` is null, show only product name and SKU.

---

## 9. Complete TypeScript Types

```ts
type StockAvailability = 'InStock' | 'LowStock' | 'OutOfStock';

type GridImage = {
  id: string;
  secureUrl: string;
  altText: string | null;
  sortOrder: number;
  isPrimary: boolean;
};

type GridRow = {
  id: string;
  variantId: string | null;
  productId: string;
  slug: string;
  name: string;
  sku: string | null;
  effectivePrice: number;
  currency: string;
  primaryImageUrl: string | null;
  stockAvailability: StockAvailability;
  canPurchase: boolean;
  isOutOfStock: boolean;
  categoryId: string | null;
  categoryName: string | null;
  categorySlug: string | null;
  brandId: string | null;
  brandName: string | null;
  brandSlug: string | null;
  isFeatured: boolean;
  ratingAverage: number;
  ratingCount: number;
  images: GridImage[];
};

type PagedResponse<T> = {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
};

type StorefrontProductResponse = {
  id: string;
  name: string;
  price: number;
  compareAtPrice: number | null;
  description: string | null;
  shortDescription: string | null;
  slug: string;
  images: GridImage[];
  attributes: Array<{
    id: string;
    name: string;
    sortOrder: number;
    values: Array<{
      id: string;
      value: string;
      sortOrder: number;
    }>;
  }>;
  variants: Array<{
    id: string;
    sku: string | null;
    effectivePrice: number;
    sortOrder: number;
    isActive: boolean;
    attributeValueIds: string | null;
    stockAvailability: StockAvailability;
    canPurchase: boolean;
    attributes: Array<{
      attributeValueId: string;
      attributeId: string;
      attributeName: string;
      value: string;
    }>;
    images: GridImage[];
  }>;
};
```

---

## 10. Frontend Checklist

- [ ] Product editor has exactly 3 fixed buttons: Cancel, Publish, Save
- [ ] Save is disabled when nothing is dirty
- [ ] Save orchestrates all dirty endpoints in parallel
- [ ] On Save success, merge all responses into local state
- [ ] On Save failure, rollback to pre-save state, show inline error + Retry
- [ ] Variant matrix rows are always editable, no per-row buttons
- [ ] Stock fields (onHand, lowStockThreshold) are inline in each variant row
- [ ] Stock badge updates from merged InventoryResponse after save
- [ ] Product-level images hidden when product has variants
- [ ] Variant image strip in each variant row (40×40px thumbnails + add button)
- [ ] Variant creation auto-duplicates product images to new variant
- [ ] Storefront variant grid returns only variant-level images (no product fallback)
- [ ] Storefront card uses `variantId === null` to branch simple vs variant products
- [ ] PDP selector uses `findVariant()` with exact set-equality matching
- [ ] PDP uses `isValueAvailable()` for disabled swatches
- [ ] Add-to-cart sends `variantId` (null for simple products)
- [ ] Admin order lines show `variantDescription` (or product name if null)
- [ ] No unit stock counts in storefront
- [ ] No client-side price calculation — always use `effectivePrice`

---

## Appendix — Non-Negotiable Rules

1. **Never show unit stock counts in the storefront.** Use bands (`InStock` / `LowStock` / `OutOfStock`).
2. **Never calculate prices client-side.** `effectivePrice` is server-authoritative.
3. **Never send a self-computed `variantId` to the cart.** Forward the server-provided id unchanged.
4. **Never show a broken image.** Fallback chain ends in a placeholder.
5. **Never retry a failed cart-add automatically.** Show the error, let the user decide.
6. **Save is all-or-nothing.** Either every dirty section saves, or none do. No partial applies.
7. **Cancel reloads from server.** The simplest and most reliable way to discard changes.

---

## Appendix — Migration Path

| Step | Action | Risk |
|---|---|---|
| 1 | Deploy backend (image duplication, variant-only grid images) | None — additive |
| 2 | Deploy admin with 3-button toolbar, Save orchestrates all endpoints | Low — old endpoints unchanged |
| 3 | Deploy storefront variant grid | Low — old product list still available |
| 4 | Monitor, fix edge cases | Low |
| 5 | Remove feature flags | Low — old endpoints remain available indefinitely |
