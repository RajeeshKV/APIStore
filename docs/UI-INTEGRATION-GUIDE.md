# UI Integration Guide — Variant System & Cart Updates

> **Crisp, actionable reference for frontend integration.** Covers only the changed/affected contracts.

---

## 1. Variant Grid — `GET /api/v1/store/products/variants`

### What Changed
- `inStockOnly` now filters **BOTH** variant rows AND simple-product fallback rows
- `CompareAtPrice` added to variant rows
- `variantAttributes` array now always populated for variants

### Request
```
GET /api/v1/store/products/variants?page=1&pageSize=20&inStockOnly=true&categorySlug=phones&sortBy=price&sortDirection=asc
```

| Parameter | Type | Default | Notes |
|-----------|------|---------|-------|
| `page` | int | 1 | ≥1 |
| `pageSize` | int | 20 | 1–100 |
| `search` | string | — | Name, SKU, description |
| `categorySlug` | string | — | Slug, not ID |
| `brandSlug` | string | — | Slug, not ID |
| `minPrice` / `maxPrice` | decimal | — | Filter by effectivePrice |
| `isFeatured` | bool? | — | |
| `inStockOnly` | bool | `false` | **Filters variants + simple products** |
| `attributeFilters` | array | — | `[{attributeName, attributeValue}]` repeatable |
| `sortBy` | string | — | `name`, `price`, `created_at` |
| `sortDirection` | string | `desc` | `asc` / `desc` |

### Response — `PagedResponse<StorefrontVariantRowResponse>`

```json
{
  "items": [
    {
      "id": "cc980af6-64d4-4f47-ac9d-13e0afc9eb04",
      "variantId": "cc980af6-64d4-4f47-ac9d-13e0afc9eb04",
      "productId": "d7959407-ec31-41f1-a008-ba4b67cfa705",
      "slug": "iphone-17-pro",
      "name": "Iphone 17 Pro",
      "sku": "IP17P-ORG-256",
      "effectivePrice": 100,
      "compareAtPrice": 134560,
      "currency": "INR",
      "primaryImageUrl": "https://res.cloudinary.com/.../wq7pct05udebfxvjsr5e.avif",
      "stockAvailability": "InStock",
      "canPurchase": true,
      "isOutOfStock": false,
      "categoryId": "fbad84da-77d0-4d25-b1bf-bc721bf0861c",
      "categoryName": "Phones",
      "categorySlug": "phones",
      "brandId": "22e3014a-6e14-4917-a3b3-84a92ff77447",
      "brandName": "Apple Inc.",
      "brandSlug": "apple-inc",
      "isFeatured": true,
      "ratingAverage": 5,
      "ratingCount": 2,
      "variantAttributes": [
        { "attributeValueId": "82891a21-4c63-4f44-a147-f3b228e5622a", "attributeId": "27d09fe7-2c1f-482d-8c75-c90a9d5387d5", "attributeName": "Color", "value": "Orange" },
        { "attributeValueId": "cf6362e1-1e0a-4819-b085-f2ee51c445f5", "attributeId": "304e4100-2bb5-499a-9917-a39696c12d59", "attributeName": "Storage", "value": "256GB" }
      ],
      "images": [
        { "id": "0b299d81-359b-49de-a03e-481f4d105d98", "url": "https://...", "altText": null, "sortOrder": 0, "isPrimary": true }
      ]
    },
    {
      "id": "90710755-3468-49e8-a3f2-e0d0905b04fc",
      "variantId": null,
      "productId": "90710755-3468-49e8-a3f2-e0d0905b04fc",
      "slug": "sony-alpha-camera",
      "name": "Sony Alpha Camera",
      "sku": null,
      "effectivePrice": 136490,
      "compareAtPrice": 182490,
      "currency": "INR",
      "primaryImageUrl": "https://res.cloudinary.com/.../chkd6kpn9pgbmtds8bqc.avif",
      "stockAvailability": "OutOfStock",
      "canPurchase": false,
      "isOutOfStock": true,
      "categoryId": "939c0e4c-9359-4501-9d66-b95cdd755277",
      "categoryName": "Cameras",
      "categorySlug": "dslr",
      "brandId": "3734e400-083f-4b72-b04f-255728c6f5ea",
      "brandName": "Sony",
      "brandSlug": "sony",
      "isFeatured": false,
      "ratingAverage": 0,
      "ratingCount": 0,
      "variantAttributes": null,
      "images": []
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 12,
  "totalPages": 1,
  "hasNextPage": false,
  "hasPreviousPage": false
}
```

### Field Reference — `StorefrontVariantRowResponse`

| Field | Type | Variant Row | Simple Product Row | Notes |
|-------|------|-------------|-------------------|-------|
| `id` | guid | Variant ID | Product ID | Use as `variantId` in cart add |
| `variantId` | guid? | Variant ID | `null` | **Key discriminator** |
| `productId` | guid | Product ID | Product ID | |
| `slug` | string | Product slug | Product slug | Route: `/products/{slug}` |
| `name` | string | Product name | Product name | |
| `sku` | string? | Variant SKU | `null` | |
| `effectivePrice` | decimal | `priceOverride ?? product.price` | `product.price` | **Display this** |
| `compareAtPrice` | decimal? | **NEW** `product.compareAtPrice` | `product.compareAtPrice` | Show strikethrough if > effectivePrice |
| `currency` | string | ISO 4217 | ISO 4217 | |
| `primaryImageUrl` | string? | Variant primary image | Product primary image | **Never mixed** |
| `stockAvailability` | enum | `InStock`/`LowStock`/`OutOfStock` | Same | Computed per row |
| `canPurchase` | bool | `isActive && available > 0` | Same | Disable Add to Cart when false |
| `isOutOfStock` | bool | `available <= 0` | Same | Convenience flag |
| `variantAttributes` | array? | **Always populated** | `null` | `[{attributeValueId, attributeId, attributeName, value}]` |
| `images` | array | Variant images only | Product images | Empty = no variant images uploaded |

### UI Rendering Rules

```typescript
// Discriminate row type
const isVariant = row.variantId !== null;

// Card link
const href = isVariant 
  ? `/products/${row.slug}?variant=${row.variantId}`
  : `/products/${row.slug}`;

// Image (G1, G3)
const imageUrl = row.primaryImageUrl ?? '/placeholder-product.png';

// Price (G4)
const price = row.effectivePrice;
const showCompareAt = row.compareAtPrice && row.compareAtPrice > price;

// Stock badge (G5)
const stockLabel = row.stockAvailability; // InStock | LowStock | OutOfStock
const isDisabled = !row.canPurchase;

// Swatches (listing only, if hasVariants)
if (isVariant && row.variantAttributes) {
  // Render first N attribute values as clickable swatches
  // OnClick → navigate to href above
}
```

---

## 2. Product Detail (PDP) — `GET /api/v1/store/products/{slug}`

### Response — `StorefrontProductResponse` (Key Fields)

```json
{
  "id": "d7959407-ec31-41f1-a008-ba4b67cfa705",
  "name": "Iphone 17 Pro",
  "slug": "iphone-17-pro",
  "price": 100,
  "compareAtPrice": 134560,
  "currency": "INR",
  "stockAvailability": "InStock",
  "canPurchase": true,
  "images": [...],
  "attributes": [
    { "id": "27d09fe7-2c1f-482d-8c75-c90a9d5387d5", "name": "Color", "sortOrder": 0, "values": [{ "id": "82891a21-4c63-4f44-a147-f3b228e5622a", "value": "Orange", "sortOrder": 0 }] },
    { "id": "304e4100-2bb5-499a-9917-a39696c12d59", "name": "Storage", "sortOrder": 1, "values": [{ "id": "cf6362e1-1e0a-4819-b085-f2ee51c445f5", "value": "256GB", "sortOrder": 0 }] }
  ],
  "variants": [
    {
      "id": "cc980af6-64d4-4f47-ac9d-13e0afc9eb04",
      "sku": "IP17P-ORG-256",
      "effectivePrice": 100,
      "compareAtPrice": 134560,
      "sortOrder": 0,
      "isActive": true,
      "attributeValueIds": "82891a21-4c63-4f44-a147-f3b228e5622a,cf6362e1-1e0a-4819-b085-f2ee51c445f5",
      "stockAvailability": "InStock",
      "canPurchase": true,
      "attributes": [
        { "attributeValueId": "82891a21-4c63-4f44-a147-f3b228e5622a", "attributeId": "27d09fe7-2c1f-482d-8c75-c90a9d5387d5", "attributeName": "Color", "value": "Orange" },
        { "attributeValueId": "cf6362e1-1e0a-4819-b085-f2ee51c445f5", "attributeId": "304e4100-2bb5-499a-9917-a39696c12d59", "attributeName": "Storage", "value": "256GB" }
      ],
      "images": [...]
    }
  ],
  "deliveryEstimate": { "minDays": 2, "maxDays": 5, "processingDays": 1 }
}
```

### Variant Resolution (Exact Set Match)

```typescript
function resolveVariant(product: StorefrontProductResponse, selection: Record<string, string>) {
  // selection = { attributeId: attributeValueId }
  const selectedIds = Object.values(selection).sort().join(',');
  
  return product.variants.find(v => {
    const variantIds = (v.attributeValueIds ?? '')
      .split(',').map(s => s.trim()).filter(Boolean).sort().join(',');
    return variantIds === selectedIds && v.isActive && v.canPurchase;
  });
}

// Default variant on load (first active + purchasable by sortOrder)
const defaultVariant = product.variants
  .filter(v => v.isActive && v.canPurchase)
  .sort((a, b) => a.sortOrder - b.sortOrder)[0];
```

### Image/Price/Stock Hierarchy

| State | Image Source | Price Source | Stock Source |
|-------|-------------|--------------|--------------|
| No variant selected | `product.images` (primary) | `product.price` | `product.stockAvailability` (rolled up) |
| Variant resolved | `resolvedVariant.images` | `resolvedVariant.effectivePrice` | `resolvedVariant.stockAvailability` |

---

## 3. Cart — Updated DELETE Endpoints

### Endpoints

| Method | Route | Auth | Returns |
|--------|-------|------|---------|
| GET | `/api/v1/cart` | None | `CartResponse` |
| POST | `/api/v1/cart/items` | None | `CartResponse` |
| PUT | `/api/v1/cart/items/{itemId}` | None | `CartResponse` |
| **DELETE** | `/api/v1/cart/items/{itemId}` | None | **`CartResponse`** (was 204) |
| **DELETE** | `/api/v1/cart` | None | **`CartResponse`** (was 204) |

### Headers
- Authenticated: `Authorization: Bearer <jwt>`
- Anonymous: `X-Cart-Token: <token>` (from first add-item response)

### Add Item Request
```json
{ "productId": "d7959407-ec31-41f1-a008-ba4b67cfa705", "variantId": "cc980af6-64d4-4f47-ac9d-13e0afc9eb04", "quantity": 1 }
```
- **Rule G6**: If product has variants (`variants.length > 0`), ALWAYS send `variantId` (never null)

### Cart Response
```json
{
  "id": "cart-guid",
  "items": [
    {
      "id": "cartitem-guid",
      "productId": "d7959407-ec31-41f1-a008-ba4b67cfa705",
      "productName": "Iphone 17 Pro",
      "productSlug": "iphone-17-pro",
      "variantId": "cc980af6-64d4-4f47-ac9d-13e0afc9eb04",
      "variantSku": "IP17P-ORG-256",
      "quantity": 2,
      "unitPrice": 100,
      "totalPrice": 200,
      "currency": "INR",
      "image": { "id": "...", "url": "https://...", "altText": null, "sortOrder": 0, "isPrimary": true },
      "variantAttributes": [
        { "attributeValueId": "...", "attributeId": "...", "attributeName": "Color", "value": "Orange" },
        { "attributeValueId": "...", "attributeId": "...", "attributeName": "Storage", "value": "256GB" }
      ],
      "stockAvailability": "InStock",
      "canPurchase": true
    }
  ],
  "subtotal": 200,
  "itemCount": 2
}
```

### Mini-Cart Item Rendering

```tsx
// Image: item.image (variant-specific if variant, else product primary)
// Price: item.unitPrice (already variant price)
// Attributes: item.variantAttributes as chips: "Color: Orange, Storage: 256GB"
// Stock: item.stockAvailability
// Link: `/products/${item.productSlug}${item.variantId ? `?variant=${item.variantId}` : ''}`
// Quantity max: backend enforces; disable increment if would exceed
// Remove: DELETE /api/v1/cart/items/{item.id} → handle CartResponse
```

---

## 4. Checkout Summary — `GET /api/v1/checkout/summary`

**Authoritative totals** (never trust cart totals).

```json
{
  "items": [...],  // Same shape as cart items
  "subtotal": 1998.00,
  "shippingAmount": 99.00,
  "codFee": 0,
  "discountAmount": 0,
  "taxAmount": 360.00,
  "grandTotal": 2457.00,
  "currency": "INR"
}
```

---

## 5. Admin — Variant Management

### Create Variant — `POST /api/v1/products/{productId}/variants`

```json
{
  "sku": "IP17P-ORG-256",
  "priceOverride": 100,
  "compareAtPrice": 134560,
  "sortOrder": 0,
  "attributeValueIds": ["82891a21-4c63-4f44-a147-f3b228e5622a", "cf6362e1-1e0a-4819-b085-f2ee51c445f5"]
}
```

### Update Variant — `PUT /api/v1/products/{productId}/variants/{variantId}`

```json
{
  "sku": "IP17P-ORG-256",
  "priceOverride": 100,
  "compareAtPrice": 134560,
  "sortOrder": 0,
  "isActive": true,
  "attributeValueIds": ["82891a21-4c63-4f44-a147-f3b228e5622a", "cf6362e1-1e0a-4819-b085-f2ee51c445f5"]
}
```
- **All fields required** (no partial updates)

### Variant Response
```json
{
  "id": "cc980af6-64d4-4f47-ac9d-13e0afc9eb04",
  "sku": "IP17P-ORG-256",
  "priceOverride": 100,
  "compareAtPrice": 134560,
  "sortOrder": 0,
  "isActive": true,
  "attributeValueIds": "82891a21-4c63-4f44-a147-f3b228e5622a,cf6362e1-1e0a-4819-b085-f2ee51c445f5",
  "availableStock": 10,
  "attributes": [
    { "attributeValueId": "82891a21-4c63-4f44-a147-f3b228e5622a", "attributeId": "27d09fe7-2c1f-482d-8c75-c90a9d5387d5", "attributeName": "Color", "value": "Orange" },
    { "attributeValueId": "cf6362e1-1e0a-4819-b085-f2ee51c445f5", "attributeId": "304e4100-2bb5-499a-9917-a39696c12d59", "attributeName": "Storage", "value": "256GB" }
  ],
  "isOutOfStock": false
}
```

---

## 6. Admin — Product Detail (by ID)

### `GET /api/v1/products/admin/{id}` → `ProductResponse`

**New field:** `baseInventory` (for products WITHOUT variants)

```json
{
  "id": "90710755-3468-49e8-a3f2-e0d0905b04fc",
  "name": "Sony Alpha Camera",
  "slug": "sony-alpha-camera",
  "price": 136490,
  "compareAtPrice": 182490,
  "status": "Published",
  "baseInventory": {
    "id": "inv-guid",
    "productId": "90710755-3468-49e8-a3f2-e0d0905b04fc",
    "variantId": null,
    "onHand": 0,
    "reserved": 0,
    "available": 0,
    "lowStockThreshold": 5,
    "isLowStock": false,
    "isOutOfStock": true,
    "updatedAt": "2026-10-09T12:00:00Z"
  },
  "variants": [],
  "attributes": []
}
```

| Product Type | `baseInventory` | `variants` |
|--------------|-----------------|------------|
| No variants | **Populated** | Empty array |
| Has variants | `null` | Populated |

---

## 7. Admin — Inventory (Stock)

### Set Absolute Stock — `PUT /api/v1/admin/inventory/{productId}?variantId=`
```json
{ "onHand": 25, "lowStockThreshold": 5 }
```

### Adjust Stock — `POST /api/v1/admin/inventory/{productId}/adjust?variantId=`
```json
{ "delta": -3, "reason": "Damaged in transit" }
```

### Response
```json
{
  "id": "inv-guid",
  "productId": "product-guid",
  "variantId": "variant-guid|null",
  "onHand": 22,
  "reserved": 3,
  "available": 19,
  "lowStockThreshold": 5,
  "isLowStock": false,
  "isOutOfStock": false,
  "updatedAt": "2026-10-09T12:00:00Z"
}
```

| Call | `variantId` | Operates On |
|------|-------------|-------------|
| Omit | — | Base product inventory |
| Provide | `guid` | That variant's inventory |

---

## 8. Admin — Product Attributes

### Upsert — `PUT /api/v1/products/{productId}/attributes`
```json
{
  "name": "Color",
  "values": [
    { "value": "Red" },
    { "value": "Blue" },
    { "id": "existing-guid", "value": "Green" }
  ]
}
```
- Keyed by `name` — creates if new, replaces value list if exists
- Values without `id` = create new; with `id` = preserve existing

---

## 9. Breaking Changes Checklist

| Change | Frontend Action Required |
|--------|-------------------------|
| `DELETE /cart/items/{itemId}` returns `CartResponse` | Handle response body; update mini-cart from it |
| `DELETE /cart` returns `CartResponse` | Same as above |
| `inStockOnly` filters simple products too | No code change; behavior is now correct |
| `VariantResponse` has `compareAtPrice` | Optional: show strikethrough in admin/variant selectors |
| `ProductResponse` has `baseInventory` | Update admin product detail screen |

---

## 10. Quick Type Definitions (Frontend)

```typescript
// Variant Grid Row
interface StorefrontVariantRow {
  id: string;
  variantId: string | null;
  productId: string;
  slug: string;
  name: string;
  sku: string | null;
  effectivePrice: number;
  compareAtPrice: number | null;
  currency: string;
  primaryImageUrl: string | null;
  stockAvailability: 'InStock' | 'LowStock' | 'OutOfStock';
  canPurchase: boolean;
  isOutOfStock: boolean;
  categoryId: string;
  categoryName: string;
  categorySlug: string;
  brandId: string;
  brandName: string;
  brandSlug: string;
  isFeatured: boolean;
  ratingAverage: number;
  ratingCount: number;
  variantAttributes: VariantAttribute[] | null;
  images: StorefrontImage[];
}

// Cart Item
interface CartItem {
  id: string;
  productId: string;
  productName: string;
  productSlug: string;
  variantId: string | null;
  variantSku: string | null;
  quantity: number;
  unitPrice: number;
  totalPrice: number;
  currency: string;
  image: StorefrontImage | null;
  variantAttributes: VariantAttribute[];
  stockAvailability: 'InStock' | 'LowStock' | 'OutOfStock';
  canPurchase: boolean;
}

// Variant Attribute (resolved)
interface VariantAttribute {
  attributeValueId: string;
  attributeId: string;
  attributeName: string;
  value: string;
}

// Storefront Image
interface StorefrontImage {
  id: string;
  url: string;
  altText: string | null;
  sortOrder: number;
  isPrimary: boolean;
}
```

---

## 11. Migration Required

```bash
# Apply new migration for variant CompareAtPrice
dotnet ef database update --project src/KromicCommerce.Infrastructure --startup-project src/KromicCommerce.Api
```
Migration: `20261009082200_AddVariantCompareAtPrice` — adds `product_variants.CompareAtPrice` column.

---

*Generated from backend contracts. Frontend must match exactly.*