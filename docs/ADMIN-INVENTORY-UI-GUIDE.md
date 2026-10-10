# Admin Inventory Management — UI Integration Guide

> **Backend is complete.** This guide tells the frontend exactly how to build the inventory editing UI.

---

## 1. Data Flow Summary

| Screen | Data Source | Write Endpoint |
|--------|-------------|----------------|
| **Product Detail (no variants)** | `GET /api/v1/products/admin/{id}` → `baseInventory` | `PUT /api/v1/admin/inventory/{productId}` (omit `variantId`) |
| **Variant List** | `GET /api/v1/products/{productId}/variants` → each variant's `availableStock` | `PUT /api/v1/admin/inventory/{productId}?variantId={variantId}` |
| **Variant Detail** | `GET /api/v1/products/{productId}/variants/{variantId}` → `availableStock` | Same as above |
| **Adjust Stock (delta)** | Any screen | `POST /api/v1/admin/inventory/{productId}/adjust?variantId=` |

---

## 2. Read Endpoints

### 2.1 Product Detail (No Variants)
```
GET /api/v1/products/admin/{productId}
```
**Response includes:**
```json
{
  "id": "product-guid",
  "name": "Product Name",
  "variants": [],
  "baseInventory": {
    "id": "inv-guid",
    "productId": "product-guid",
    "variantId": null,
    "onHand": 10,
    "reserved": 2,
    "available": 8,
    "lowStockThreshold": 5,
    "isLowStock": false,
    "isOutOfStock": false,
    "updatedAt": "2026-10-10T10:00:00Z"
  }
}
```
- `baseInventory` is **only present when `variants` is empty**
- Use `onHand` for the editable field (what admin sets)
- `available` = `onHand - reserved` (what customers can buy)
- `reserved` = units in active carts/checkouts (read-only)

### 2.2 Variant List
```
GET /api/v1/products/{productId}/variants
```
**Response:**
```json
[
  {
    "id": "variant-guid",
    "sku": "SKU-001",
    "priceOverride": 100,
    "compareAtPrice": 150,
    "sortOrder": 0,
    "isActive": true,
    "attributeValueIds": "guid1,guid2",
    "availableStock": 8,
    "attributes": [
      { "attributeValueId": "...", "attributeId": "...", "attributeName": "Color", "value": "Red" },
      { "attributeValueId": "...", "attributeId": "...", "attributeName": "Size", "value": "M" }
    ],
    "images": [...]
  }
]
```
- `availableStock` = current `available` (onHand - reserved)
- `null` means no inventory row exists yet (treat as 0 for display, but distinguish for UX)

### 2.3 Single Variant
```
GET /api/v1/products/{productId}/variants/{variantId}
```
Same shape as list item, plus full `images` array.

---

## 3. Write Endpoints

### 3.1 Set Absolute Stock
```
PUT /api/v1/admin/inventory/{productId}?variantId={variantId}
```
**Request:**
```json
{
  "onHand": 25,
  "lowStockThreshold": 5
}
```
| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `onHand` | int | **Yes** | Absolute quantity (replaces current) |
| `lowStockThreshold` | int | No (default 5) | ≤ this → `isLowStock = true` |

**Query Param:**
- `variantId` (optional) — omit for base product, provide for variant

**Response:** `InventoryResponse` (see §2)

**Errors:**
| Code | Cause |
|------|-------|
| `404 PRODUCT_NOT_FOUND` | Product doesn't exist |
| `404 VARIANT_NOT_FOUND` | Variant doesn't exist or doesn't belong to product |
| `404 INVENTORY_NOT_FOUND` | No inventory row exists (for adjust only) |
| `400 STOCK_ADJUSTMENT_INVALID` | `onHand < reserved` (cannot go below reserved) |

### 3.2 Adjust Stock (Delta)
```
POST /api/v1/admin/inventory/{productId}/adjust?variantId={variantId}
```
**Request:**
```json
{
  "delta": -3,
  "reason": "Damaged in transit"
}
```
| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `delta` | int | **Yes** | Positive = restock, negative = reduce |
| `reason` | string | No | Audit trail |

**Response:** `InventoryResponse` (updated state)

**Errors:** Same as Set Stock + `onHand` cannot go negative.

---

## 4. UI Components

### 4.1 Product Inventory Card (No Variants)

```tsx
interface ProductInventoryCardProps {
  productId: string;
  baseInventory: InventoryResponse;  // from GET /admin/products/{id}
  onSave: (productId: string, variantId: null, onHand: number, lowStockThreshold: number) => Promise<void>;
}

function ProductInventoryCard({ productId, baseInventory, onSave }) {
  const [onHand, setOnHand] = useState(baseInventory.onHand);
  const [threshold, setThreshold] = useState(baseInventory.lowStockThreshold);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Enable Save when onHand actually changed
  const isDirty = onHand !== baseInventory.onHand || threshold !== baseInventory.lowStockThreshold;

  const handleSave = async () => {
    setSaving(true);
    setError(null);
    try {
      await onSave(productId, null, onHand, threshold);
      // Success: baseInventory will be refreshed from parent
    } catch (e) {
      setError(e.response?.data?.error?.message ?? 'Failed to save stock');
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="inventory-card">
      <h3>Base Product Stock</h3>
      
      <div className="stock-fields">
        <div className="field">
          <label>On Hand (editable)</label>
          <input
            type="number"
            min="0"
            value={onHand}
            onChange={e => setOnHand(parseInt(e.target.value) || 0)}
            disabled={saving}
          />
        </div>
        
        <div className="field">
          <label>Low Stock Threshold</label>
          <input
            type="number"
            min="0"
            value={threshold}
            onChange={e => setThreshold(parseInt(e.target.value) || 5)}
            disabled={saving}
          />
        </div>
      </div>

      <div className="stock-readonly">
        <span>Reserved: {baseInventory.reserved}</span>
        <span>Available: {baseInventory.available}</span>
        <Badge variant={baseInventory.isOutOfStock ? 'danger' : baseInventory.isLowStock ? 'warning' : 'success'}>
          {baseInventory.isOutOfStock ? 'Out of Stock' : baseInventory.isLowStock ? 'Low Stock' : 'In Stock'}
        </Badge>
      </div>

      <button onClick={handleSave} disabled={!isDirty || saving}>
        {saving ? 'Saving...' : 'Save Stock'}
      </button>
      
      {error && <Alert variant="error">{error}</Alert>}
    </div>
  );
}
```

### 4.2 Variant Inventory Row (In Variant List)

```tsx
interface VariantInventoryRowProps {
  variant: VariantResponse;  // from GET /products/{id}/variants
  onSave: (productId: string, variantId: string, onHand: number, lowStockThreshold: number) => Promise<void>;
}

function VariantInventoryRow({ variant, onSave }) {
  const [onHand, setOnHand] = useState(variant.availableStock ?? 0);
  const [threshold, setThreshold] = useState(5);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const isDirty = onHand !== (variant.availableStock ?? 0);

  const handleSave = async () => {
    setSaving(true);
    setError(null);
    try {
      await onSave(variant.productId, variant.id, onHand, threshold);
    } catch (e) {
      setError(e.response?.data?.error?.message ?? 'Failed to save');
    } finally {
      setSaving(false);
    }
  };

  return (
    <tr>
      <td>{variant.attributes?.map(a => a.value).join(', ')}</td>
      <td>{variant.sku}</td>
      <td>
        <input
          type="number"
          min="0"
          value={onHand}
          onChange={e => setOnHand(parseInt(e.target.value) || 0)}
          disabled={saving}
          style={{ width: '80px' }}
        />
      </td>
      <td>{variant.availableStock ?? '—'}</td>
      <td>
        <Badge variant={variant.isOutOfStock ? 'danger' : 'success'}>
          {variant.isOutOfStock ? 'Out' : 'In Stock'}
        </Badge>
      </td>
      <td>
        <button onClick={handleSave} disabled={!isDirty || saving} className="btn-sm">
          {saving ? '⋯' : 'Save'}
        </button>
        {error && <span className="error">{error}</span>}
      </td>
    </tr>
  );
}
```

### 4.3 Stock Adjustment Modal (Quick +/-)

```tsx
interface AdjustStockModalProps {
  productId: string;
  variantId: string | null;
  currentOnHand: number;
  onClose: () => void;
  onSuccess: () => void;
}

function AdjustStockModal({ productId, variantId, currentOnHand, onClose, onSuccess }) {
  const [delta, setDelta] = useState(0);
  const [reason, setReason] = useState('');
  const [saving, setSaving] = useState(false);

  const handleSubmit = async () => {
    if (delta === 0) return;
    setSaving(true);
    try {
      await api.post(`/admin/inventory/${productId}/adjust?variantId=${variantId ?? ''}`, {
        delta,
        reason
      });
      onSuccess();
      onClose();
    } catch (e) {
      alert(e.response?.data?.error?.message ?? 'Adjustment failed');
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal title={`Adjust Stock (${variantId ? 'Variant' : 'Base Product'})`} onClose={onClose}>
      <p>Current On Hand: <strong>{currentOnHand}</strong></p>
      <p>New On Hand will be: <strong>{currentOnHand + delta}</strong></p>
      
      <div className="field">
        <label>Delta (+/-)</label>
        <input
          type="number"
          value={delta}
          onChange={e => setDelta(parseInt(e.target.value) || 0)}
          placeholder="+10 or -3"
        />
      </div>
      
      <div className="field">
        <label>Reason (optional)</label>
        <input type="text" value={reason} onChange={e => setReason(e.target.value)} />
      </div>
      
      <button onClick={handleSubmit} disabled={saving || delta === 0}>
        {saving ? 'Saving...' : 'Apply Adjustment'}
      </button>
    </Modal>
  );
}
```

---

## 5. Save Button Enablement Logic

**The Save button should be enabled ONLY when the user has actually changed a value:**

```typescript
// For base product
const isDirty = onHand !== baseInventory.onHand || threshold !== baseInventory.lowStockThreshold;

// For variant
const isDirty = onHand !== (variant.availableStock ?? 0);
```

This is the key UX requirement: **no save = no API call**.

---

## 6. Complete API Reference for UI

### Read
| Endpoint | Returns | Use For |
|----------|---------|---------|
| `GET /api/v1/products/admin/{id}` | `ProductResponse` with `baseInventory` | Product detail page (no variants) |
| `GET /api/v1/products/{productId}/variants` | `VariantResponse[]` with `availableStock` | Variant list/table |
| `GET /api/v1/products/{productId}/variants/{variantId}` | `VariantResponse` | Variant detail/edit |

### Write
| Endpoint | Method | Body | When to Use |
|----------|--------|------|-------------|
| `/api/v1/admin/inventory/{productId}` | PUT | `{ onHand, lowStockThreshold? }` | Set absolute stock (primary) |
| `/api/v1/admin/inventory/{productId}/adjust` | POST | `{ delta, reason? }` | Quick +/- adjustments |

**Always include `?variantId=` query param for variants, omit for base product.**

---

## 7. TypeScript Types

```typescript
interface InventoryResponse {
  id: string;
  productId: string;
  variantId: string | null;
  onHand: number;
  reserved: number;
  available: number;
  lowStockThreshold: number;
  isLowStock: boolean;
  isOutOfStock: boolean;
  updatedAt: string;  // ISO 8601
}

interface SetStockRequest {
  onHand: number;
  lowStockThreshold?: number;  // default 5
}

interface AdjustStockRequest {
  delta: number;  // positive = restock, negative = reduce
  reason?: string;
}

interface VariantResponse {
  id: string;
  sku: string | null;
  priceOverride: number | null;
  compareAtPrice: number | null;
  sortOrder: number;
  isActive: boolean;
  attributeValueIds: string | null;
  availableStock: number | null;  // null = no inventory row yet
  attributes: VariantAttribute[];
  images: ProductImageDto[];
}

interface VariantAttribute {
  attributeValueId: string;
  attributeId: string;
  attributeName: string;
  value: string;
}
```

---

## 8. Validation Rules (Backend Enforced)

| Rule | Error Code | Message |
|------|------------|---------|
| `onHand < reserved` | `STOCK_ADJUSTMENT_INVALID` | Cannot set below reserved quantity |
| `onHand < 0` | `STOCK_ADJUSTMENT_INVALID` | On-hand cannot be negative |
| `delta` makes onHand negative | `STOCK_ADJUSTMENT_INVALID` | Adjustment would result in negative stock |
| Product not found | `PRODUCT_NOT_FOUND` | — |
| Variant not found / wrong product | `VARIANT_NOT_FOUND` | — |
| Inventory row missing (adjust only) | `INVENTORY_NOT_FOUND` | — |

---

## 9. Integration Checklist

- [ ] Product detail page reads `baseInventory` from `GET /admin/products/{id}`
- [ ] Variant list reads `availableStock` from `GET /products/{id}/variants`
- [ ] Save button **only enabled when value actually changed** (`isDirty`)
- [ ] Save calls `PUT /admin/inventory/{productId}` with `onHand` and `lowStockThreshold`
- [ ] For variants, pass `?variantId=` query param
- [ ] Optional: "Adjust" button opens modal calling `POST /admin/inventory/{productId}/adjust`
- [ ] On success, refresh the relevant read endpoint to show updated `available`, `isLowStock`, `isOutOfStock`
- [ ] Show `reserved` as read-only context (units in carts)
- [ ] Badge: `isOutOfStock` → red, `isLowStock` → yellow, else green

---

## 10. Example Complete Flow

```
1. Admin opens Product Detail (no variants)
   → GET /api/v1/products/admin/abc-123
   → Shows baseInventory.onHand = 10

2. Admin changes On Hand to 25, clicks Save
   → PUT /api/v1/admin/inventory/abc-123 { onHand: 25, lowStockThreshold: 5 }
   → Returns updated InventoryResponse { onHand: 25, available: 23, ... }

3. UI updates display from response
   → Shows On Hand: 25, Available: 23, Badge: "In Stock"
```

```
1. Admin opens Variant List for product with variants
   → GET /api/v1/products/abc-123/variants
   → Shows table with each variant's availableStock

2. Admin edits variant "Red/M" onHand from 8 → 15, clicks Save
   → PUT /api/v1/admin/inventory/abc-123?variantId=var-456 { onHand: 15 }
   → Returns updated InventoryResponse

3. UI updates that row's Available from 8 → 15
```

---

*Backend endpoints are live and tested. Implement the UI components above.*