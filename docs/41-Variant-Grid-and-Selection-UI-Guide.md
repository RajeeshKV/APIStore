# Variant Grid and Selection — UI Integration Guide

> **Source of truth for the frontend team.** Every field name, endpoint path and behavioural
> rule in this document is taken from the running backend code, not from an idealised design.
> If something here contradicts the API, the API wins — raise a doc bug.

---

## 1. Overview: two endpoints, one data model

The storefront has two product-listing endpoints:

| Endpoint | Returns | Use |
|---|---|---|
| `GET /api/v1/store/products` | One row per **product** | Legacy / simple catalogs |
| `GET /api/v1/store/products/variants` | One row per **active variant**; products without variants appear once | **Variant-aware grids** |

Both endpoints accept the same query-string parameters (`search`, `categorySlug`, `brandSlug`,
`minPrice`, `maxPrice`, `isFeatured`, `inStockOnly`, `sortBy`, `sortDirection`, `page`,
`pageSize`). Wire your existing query builder to either endpoint — the parameter names are
identical.

### 1.1 What the variant grid returns for mixed catalogs

A catalog with:
- 1 product that has 6 active variants
- 2 products with no variants

→ **8 rows** from `/variants`. `totalCount: 8`.

Each row carries a `variantId` field that tells the frontend which case it is:

```
variantId = "var-001"  →  one combination of a variant product
variantId = null       →  a simple product (no variants exist)
```

There is no separate endpoint for simple products. Use `/variants` for everything.

### 1.2 Why totalCount can exceed your product count

`totalCount` is the row count, not the product count. A product with 3 variants contributes 3 to
the total. Your paginator should display page information based on the row count:

```
Page 1 of 4  (8 items)
```

Not:

```
Page 1 of 3  (3 products)
```

The `items` array length equals `pageSize` (except the last page). Each item is self-contained —
you can render it without looking at any other item.

---

## 2. Variant Grid endpoint

### 2.1 Request

```
GET /api/v1/store/products/variants?search=t-shirt&categorySlug=shirts&inStockOnly=true&page=1&pageSize=20
```

| Parameter | Type | Default | Notes |
|---|---|---|---|
| `page` | integer | `1` | |
| `pageSize` | integer | `20` | Max 100 |
| `search` | string | null | Matches product name, shortDescription, SKU |
| `categorySlug` | string | null | **Slug**, not id |
| `brandSlug` | string | null | **Slug**, not id |
| `minPrice` | decimal | null | Variant effective price |
| `maxPrice` | decimal | null | Variant effective price |
| `isFeatured` | bool? | null | |
| `inStockOnly` | bool | `false` | Hides rows where `canPurchase = false` |
| `attributeFilters` | array | null | See §2.4 |
| `sortBy` | string | null | `name`, `price`, `created_at` |
| `sortDirection` | string | `desc` | `asc` or `desc` |

An unrecognised `sortBy` returns `400 INVALID_SORT_FIELD`.

### 2.2 Response

```jsonc
{
  "items": [
    {
      "id": "var-001",                  // always set — the row's own id (variant id)
      "variantId": "var-001",           // null for products without variants
      "productId": "P1",
      "slug": "classic-t-shirt",        // product slug — link to PDP
      "name": "Classic T-Shirt",
      "sku": "TSH-RED-S",               // variant SKU; null when not set
      "effectivePrice": 999.00,          // priceOverride ?? product.price — already resolved
      "currency": "INR",
      "primaryImageUrl": "https://…",    // product primary image (variant image if configured)
      "stockAvailability": "InStock",    // "InStock" | "LowStock" | "OutOfStock"
      "canPurchase": true,               // isActive && availableStock > 0
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
        {
          "id": "img-001",
          "secureUrl": "https://…",
          "altText": "Red shirt on white",
          "sortOrder": 0,
          "isPrimary": true
        }
      ]
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 8,
  "totalPages": 1,
  "hasNextPage": false,
  "hasPreviousPage": false
}
```

### 2.3 Field-by-field rules

| Field | Meaning | UI rule |
|---|---|---|
| `id` | The row's own id. For variant rows this is the variant id. For simple-product rows this is the product id. | Use this as the key in `map()`. POST this as `variantId` to cart. |
| `variantId` | Nullable variant id. `null` = simple product. | Branch rendering on this field. |
| `slug` | Product slug. | Link to `/products/{slug}`. |
| `effectivePrice` | Final selling price, already resolved. | Render this only. Never show `product.price` alongside it. |
| `canPurchase` | `isActive && availableStock > 0`. | Gate the add-to-cart button on this field. |
| `stockAvailability` | Band: `InStock` / `LowStock` / `OutOfStock`. | Render as a badge. Do not show unit counts. |
| `primaryImageUrl` | Product primary image URL, or variant primary if one exists. | Fallback when `images` is empty. |
| `images` | Variant-level images ordered by `sortOrder`. Empty array when none uploaded. | Prefer over `primaryImageUrl`. |
| `sku` | Variant SKU. Null when not set. | Show on the card; fall back to product SKU if needed. |

### 2.4 Attribute filters

```
GET /api/v1/store/products/variants?attributeFilters[0][attributeName]=Colour&attributeFilters[0][attributeValue]=Red
```

Multiple filters with the same `attributeName` are OR-combined. Different names are AND-combined.

Example: `Colour=Black OR White AND Size=M` returns products that have at least one variant
matching Black/M or White/M.

The grid endpoint applies attribute filters at the **product** level — a product passes the filter
if any of its variants covers the requested combination. This means the grid can return more rows
than the strict filter suggests (a product with a matching variant shows all its variants, not just
the matching one). This is intentional: the grid shows every purchasable combination, and the
filter is a product-level inclusion test, not a row-level one.

### 2.5 In-stock filter

`inStockOnly=true` removes every row where `canPurchase = false`. This includes:
- Variants with zero available stock
- Inactive variants (already excluded by the grid, but the filter is an additional guard)

Simple products with no stock also disappear when `inStockOnly=true`.

### 2.6 Sort

| `sortBy` | `sortDirection=asc` | `sortDirection=desc` |
|---|---|---|
| `name` | Product name A→Z | Product name Z→A |
| `price` | Effective price low→high | Effective price high→low |
| `created_at` | Oldest first | Newest first |

Any other `sortBy` value → `400 INVALID_SORT_FIELD`.

### 2.7 Pagination

- `pageSize` max 100.
- `totalCount` includes both variant rows and simple-product fallback rows.
- `hasNextPage` / `hasPreviousPage` are booleans.
- Page numbers are 1-indexed.

### 2.8 Mixed catalogs: the guaranteed rules

Given a catalog with any mix of variant and non-variant products:

1. Every **active** product appears at least once.
2. Products with variants appear once per **active** variant.
3. Products without variants appear once with `variantId: null`.
4. `totalCount === items.length` on every page (the server does not lie about the count).
5. The same product can appear multiple times (once per variant). Do not deduplicate by
   `productId` on the frontend — the grid is intentionally flat.

---

## 3. Card component

### 3.1 Single component, two render paths

```tsx
type GridRow = {
  id: string;                   // row id — POST this as variantId to cart
  variantId: string | null;     // null = simple product
  productId: string;
  slug: string;                 // product slug
  name: string;
  sku: string | null;
  effectivePrice: number;
  currency: string;
  primaryImageUrl: string | null;
  stockAvailability: 'InStock' | 'LowStock' | 'OutOfStock';
  canPurchase: boolean;
  isOutOfStock: boolean;
  categorySlug: string | null;
  brandSlug: string | null;
  isFeatured: boolean;
  ratingAverage: number;
  ratingCount: number;
  images: Array<{
    id: string;
    secureUrl: string;
    altText: string | null;
    sortOrder: number;
    isPrimary: boolean;
  }>;
};

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

        {/* Combination label — only for variant rows that have images with altText */}
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
          variantId={row.variantId}      // null for simple products
          label={isSimple ? 'Add to Cart' : 'Add to Cart'}
        />
      </div>
    </Card>
  );
}
```

### 3.2 Image fallback (frontend)

The grid endpoint already resolves `primaryImageUrl` using this order:

```
1. Product-level primary image (variantId = null, isPrimary = true)
2. First product-level image by sortOrder
3. null
```

The `images` array on the row is the variant's own gallery. The frontend fallback is:

```ts
const imageUrl = row.images.find(i => i.isPrimary)?.secureUrl
  ?? row.images[0]?.secureUrl
  ?? row.primaryImageUrl
  ?? '/placeholder.png';
```

Never lazy-load these images — they arrive with the page payload as lightweight URL strings.

### 3.3 Stock badge rendering

```tsx
function StockBadge({ availability }: { availability: string }) {
  switch (availability) {
    case 'InStock':
      return <span className="badge badge-success">In Stock</span>;
    case 'LowStock':
      return <span className="badge badge-warning">Low Stock</span>;
    case 'OutOfStock':
      return <span className="badge badge-error">Out of Stock</span>;
  }
}
```

Do **not** display unit counts in the grid. The storefront never exposes exact stock numbers.

### 3.4 Button state

```tsx
<button
  disabled={!row.canPurchase}
  className={row.canPurchase ? 'btn-primary' : 'btn-disabled'}
>
  {row.canPurchase ? 'Add to Cart' : 'Out of Stock'}
</button>
```

`canPurchase` already encodes both `isActive` and stock availability. Do not add your own stock
check on top of it.

---

## 4. Product Detail Page — Variant Selector

### 4.1 Fetching the data

```
GET /api/v1/store/products/{slug}
```

The response includes the product's full variant list with resolved attribute labels and
variant-level images.

### 4.2 Selector data model

```ts
type StorefrontProductResponse = {
  id: string;
  name: string;
  price: number;                    // product base price — ignore when a variant is selected
  compareAtPrice: number | null;
  description: string | null;
  shortDescription: string | null;
  slug: string;
  images: Array<{
    id: string;
    secureUrl: string;
    altText: string | null;
    sortOrder: number;
    isPrimary: boolean;
  }>;
  attributes: Array<{
    id: string;                     // attribute id (e.g. "a-colour")
    name: string;                   // "Colour"
    sortOrder: number;
    values: Array<{
      id: string;                   // value id (e.g. "v-red")
      value: string;                // "Red"
      sortOrder: number;
    }>;
  }>;
  variants: Array<{
    id: string;
    sku: string | null;
    effectivePrice: number;
    sortOrder: number;
    isActive: boolean;
    attributeValueIds: string | null;   // "v-red,v-small"
    stockAvailability: 'InStock' | 'LowStock' | 'OutOfStock';
    canPurchase: boolean;
    attributes: Array<{
      attributeValueId: string;
      attributeId: string;
      attributeName: string;
      value: string;
    }>;
    images: Array<{
      id: string;
      secureUrl: string;
      altText: string | null;
      sortOrder: number;
      isPrimary: boolean;
    }>;
  }>;
  // ... rating, category, brand, delivery fields
};
```

### 4.3 Selector state machine

```ts
const [selection, setSelection] = useState<Record<string, string>>({});
// selection = { "a-colour": "v-red", "a-size": "v-small" }
```

**Step-by-step:**

1. Render one button group per attribute in `product.attributes`, ordered by `sortOrder`.
2. Within each group, render one button per value, ordered by `sortOrder`.
3. When the user clicks a value: `setSelection(prev => ({ ...prev, [attributeId]: valueId }))`.
4. Resolve the selected variant using exact set-equality (see §4.4).
5. Display the resolved variant's `effectivePrice` and stock status.
6. Gate the add-to-cart button on `resolved?.canPurchase`.

### 4.4 Variant resolution

```ts
function findVariant(
  variants: StorefrontProductResponse['variants'],
  selection: Record<string, string>,
): StorefrontProductResponse['variants'][number] | undefined {
  const wanted = Object.values(selection).sort();
  return variants.find(v => {
    const ids = (v.attributeValueIds ?? '').split(',').map(s => s.trim()).filter(Boolean).sort();
    return ids.length === wanted.length && ids.every((id, i) => id === wanted[i]);
  });
}
```

This is **exact set equality, order-independent**. `{v-red, v-small}` and `{v-small, v-red}` resolve
to the same variant. A partial selection (only colour chosen, no size) returns `undefined`.

### 4.5 Disabled swatches

```ts
function isValueAvailable(
  variants: StorefrontProductResponse['variants'],
  selection: Record<string, string>,
  valueId: string,
): boolean {
  return variants.some(v => {
    const ids = (v.attributeValueIds ?? '').split(',').map(s => s.trim()).filter(Boolean);
    if (!ids.includes(valueId) || !v.canPurchase) return false;
    return Object.entries(selection)
      .filter(([axis, id]) => id !== valueId)
      .every(([, id]) => ids.includes(id));
  });
}
```

A value is available if **some** purchasable variant contains it AND agrees with the rest of the
current selection. Render unavailable values with reduced opacity and disabled pointer events.

### 4.6 Image switching on selection

```ts
const resolved = findVariant(product.variants, selection);

const heroImage = resolved
  ? (resolved.images.find(i => i.isPrimary) ?? resolved.images[0])
  : product.images.find(i => i.isPrimary) ?? product.images[0];
```

When the user selects a different combination, update the main PDP image immediately. If the
resolved variant has no images, fall back to the product image.

### 4.7 Incomplete combinations

The backend does **not** require every variant to specify a value for every axis. A product with
`{Colour, Size}` can have a variant `{Red}` with no size. Once a size is selected, `{Red}` can
never resolve — it becomes unreachable stock.

**UI requirement:** after loading the PDP, check every variant's `attributeValueIds` length
against `product.attributes.length`. If any variant is shorter, display this warning above the
selector:

> Some combinations are incomplete and cannot be selected. Contact the store administrator.

Do **not** silently drop the incomplete variant — the admin needs visibility into it.

### 4.8 Price display during selection

```tsx
const resolved = findVariant(product.variants, selection);

<div className="price-display">
  {resolved
    ? formatPrice(resolved.effectivePrice, currency)
    : formatPrice(product.price, currency)}
</div>
```

Show the product's base price when nothing is selected. Switch to the variant's `effectivePrice`
once a combination resolves. Never show both prices simultaneously.

---

## 5. Add to Cart

### 5.1 Request

```jsonc
POST /api/v1/cart/items
{
  "productId": "P1",
  "variantId": "var-001",    // required when product has variants; null for simple products
  "quantity": 1
}
```

### 5.2 Server-side validation

The server checks, in order:

1. `variantId` is supplied when the product has variants. If omitted → `400 MISSING_VARIANT`.
2. The variant exists and belongs to the product. If not → `404 VARIANT_NOT_FOUND`.
3. The variant is active (`isActive = true`). If not → `409 VARIANT_NOT_ACTIVE`.
4. Sufficient stock: `(onHand - reserved) >= quantity`. If not → `409 INSUFFICIENT_STOCK`.

### 5.3 Frontend rules

| Situation | Behaviour |
|---|---|
| User clicks "Add to Cart" with no variant selected | Highlight the selector, show "Please select an option" inline. Do not call the API. |
| User clicks with a variant selected | Call the API immediately. |
| API returns `409 INSUFFICIENT_STOCK` | Show "Only N left" or "Out of stock" next to the button. Do not retry. |
| API returns `409 VARIANT_NOT_ACTIVE` | Show "This combination is currently unavailable." |
| API returns `404 VARIANT_NOT_FOUND` | Show "This combination no longer exists. Please refresh." |
| API returns `201` | Update cart count, show brief confirmation. |
| User navigates away and back | Restore `selection` from local component state (not from the server). |

### 5.4 Cart line shape

Each cart line stores:

```jsonc
{
  "productId": "P1",
  "variantId": "var-001",       // null for simple products
  "sku": "TSH-RED-S",
  "productName": "Classic T-Shirt",
  "unitPrice": 999.00,
  "quantity": 1,
  "lineTotal": 999.00
}
```

The cart summary should show `variantId !== null` rows with the variant SKU and resolved price.
Simple-product rows show only the product name and SKU.

---

## 6. Admin — Variant Image Management

### 6.1 Upload

```
POST /api/v1/products/{productId}/variants/{variantId}/images
Content-Type: multipart/form-data
```

- Max 10 files per request, 10 MB per file.
- Accepted MIME types: `image/jpeg`, `image/png`, `image/webp`, `image/gif`, `image/avif`.
- Cloudinary folder: `products/{productId}` (same as product images).
- First image uploaded for a variant becomes primary automatically.

### 6.2 Reorder / set-primary / delete

| Action | Method | Route |
|---|---|---|
| Reorder | `PUT` | `/api/v1/products/{productId}/variants/{variantId}/images/reorder` |
| Set primary | `PUT` | `/api/v1/products/{productId}/variants/{variantId}/images/{imageId}/set-primary` |
| Delete | `DELETE` | `/api/v1/products/{productId}/variants/{variantId}/images/{imageId}` |

All return `ProductImageDto` (upload, reorder, set-primary) or `204 NoContent` (delete).

### 6.3 Where to put the upload UI in the admin panel

**Recommended: inside the variant matrix.** Each variant row has a thumbnail. Clicking the
thumbnail opens a file picker scoped to that variant. After upload, the thumbnail updates
immediately from the response.

Do **not** put variant image upload inside the product-level image tab. That tab manages the
product gallery (`VariantId = null`). Mixing the two scopes will confuse operators.

### 6.4 Deleting a variant

`DELETE /api/v1/products/{productId}/variants/{variantId}` removes the variant, its inventory
item, and its variant-scoped images. Product-level images are untouched.

---

## 7. Image mapping strategy

### 7.1 The schema change

`product_images` now has an optional `VariantId` column. Product-level rows have `VariantId = null`.
Variant-level rows have `VariantId` set to the owning variant's id.

A filtered unique index on `(ProductId, VariantId, SortOrder)` prevents duplicate sort positions
within a variant's gallery. Product-level rows are excluded from this index by the filter
`"VariantId" IS NOT NULL`.

### 7.2 Image types

| Type | `VariantId` | Owned by | Deleted when |
|---|---|---|---|
| Product gallery | `null` | Product | Variant deletion does **not** touch these |
| Variant gallery | set | Variant | Variant deletion removes these |

A product-level image and a variant-level image can share the same Cloudinary asset (`PublicId`).
The backend does not deduplicate by asset.

### 7.3 Frontend fallback chain

When rendering any card or PDP hero image:

```
1. variant.images.find(i => i.isPrimary)       ← variant-specific, preferred
2. variant.images[0]                            ← variant-specific, any
3. product.images.find(i => i.isPrimary && i.variantId == null)  ← product primary
4. product.images.find(i => i.variantId == null)  ← product gallery, any
5. placeholder                                  ← last resort
```

The grid endpoint pre-resolves step 3 into `primaryImageUrl`. The PDP gives you the full
`product.images` array so you can run the full chain yourself.

### 7.4 Cloudinary folder layout

```
products/{productId}/
  ├─ img-abc123.jpg    (product-level image, VariantId = null)
  ├─ img-def456.jpg    (variant-level image, VariantId = var-001)
  └─ img-ghi789.jpg    (variant-level image, VariantId = var-002)
```

All images for a product — regardless of scope — live in the same folder. The database `VariantId`
column disambiguates ownership. There is no per-variant subfolder.

---

## 8. Admin order management — variant visibility

No API changes were needed. Order items already snapshot the variant at purchase time.

### 8.1 Order item response

```jsonc
{
  "orderItems": [
    {
      "productId": "P1",
      "variantId": "var-001",           // null for simple products
      "sku": "TSH-RED-S",               // SKU at time of purchase — immutable
      "variantDescription": "Red / Small",  // attribute labels at time of purchase — immutable
      "productName": "Classic T-Shirt",
      "unitPrice": 999.00,
      "quantity": 1,
      "lineTotal": 999.00
    }
  ]
}
```

### 8.2 What the admin sees

| Field | Meaning |
|---|---|
| `variantId` | The chosen combination's id. `null` = simple product. |
| `sku` | The variant's SKU at purchase time. Edits to the variant SKU do not propagate to existing orders. |
| `variantDescription` | Human-readable label like "Red / Small". Built from the attribute values that were current when the order was placed. Renames or deletes of attribute values do not propagate. |
| `unitPrice` | The price the customer paid. Variant price overrides that are edited later do not propagate. |

**Admin UI:** show `variantDescription` as the line title. If `variantId` is null, show only the
product name and SKU. Do not link `variantId` to a variant edit screen — the variant may have been
deleted or renamed since purchase.

---

## 9. Mixed-catalog scenarios and edge cases

### 9.1 All simple products

`/variants` returns one row per product. `variantId` is `null` on every row. The grid behaves
exactly like the product list.

### 9.2 All variant products

`/variants` returns one row per active variant. `variantId` is set on every row. No fallback rows
are created.

### 9.3 Mixed catalog

`/variants` returns variant rows + one fallback row per simple product. `totalCount` is the sum of
both. Do not deduplicate or group by `productId` on the frontend unless you explicitly want to.

### 9.4 Product with variants, all inactive

The grid returns zero rows for that product. It does not appear as a fallback row. Inactive
variants are not visible to the storefront.

### 9.5 Product with variants, none with stock

`inStockOnly=false` (default): all active variants appear, `canPurchase: false`, `stockAvailability: OutOfStock`.
`inStockOnly=true`: the product does not appear at all.

### 9.6 Variant with `priceOverride = 0`

Valid. `effectivePrice` is `0`. `canPurchase` depends on stock. Show "Free" or the currency symbol
with no amount.

### 9.7 Variant with no SKU

`sku` is `null`. The cart falls back to the product SKU at read time. The grid shows `null` — the
frontend may display "—" or hide the SKU field for that row.

---

## 10. Complete TypeScript types

```ts
// ---- Grid row ----

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

// ---- Paged response ----

type PagedResponse<T> = {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
};

// ---- PDP ----

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

## 11. Frontend checklist

- [ ] Use `GET /api/v1/store/products/variants` for all product-grid pages (shop, category, search, featured)
- [ ] Render one `ProductCard` per row; do not group by `productId` unless you have a specific UX reason
- [ ] Gate add-to-cart on `canPurchase`, not on stock count or product-level availability
- [ ] Show `stockAvailability` as a badge (`InStock` / `LowStock` / `OutOfStock`)
- [ ] Branch on `variantId === null` to handle simple products without a selector
- [ ] Implement `findVariant()` on the PDP for selector resolution
- [ ] Implement `isValueAvailable()` for greyed-out swatches
- [ ] Switch PDP hero image on variant selection using the variant's `images` array
- [ ] Show incomplete-combination warning on the PDP when any variant has fewer attribute values than declared axes
- [ ] Send `variantId` in all cart-add requests (null for simple products)
- [ ] Show `variantDescription` in admin order lines; show "simple product" label when `variantId` is null
- [ ] Add variant image upload to the admin variant matrix screen (not the product image tab)
- [ ] Fall back to `primaryImageUrl` then placeholder when `images` is empty
- [ ] Paginate based on `totalCount` (row count), not on product count
- [ ] Handle `LowStock` threshold display if desired (optional)
- [ ] Do not expose unit stock counts in the storefront
- [ ] Do not cache or reuse `variantId` across page navigations — re-resolve from the API response
