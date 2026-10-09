# Product Variant System - Technical Implementation Plan

## Executive Summary

After thorough gap analysis of the existing KromicCommerce codebase, **the backend architecture already supports all 6 requirements** with minimal to no modifications needed. The frontend integration guide below details how to leverage existing endpoints and data structures.

---

## 1. Storefront Product Display

### Requirement
- Display total count of available variants per product
- Display concatenated variant attributes (e.g., "Model, Color, Capacity")

### Gap Analysis: **NO BACKEND CHANGES REQUIRED**

### Existing Backend Support
| Endpoint | Handler | Response | Key Fields |
|----------|---------|----------|------------|
| `GET /api/v1/store/products/variants` | `GetStorefrontVariantGridHandler` | `PagedResponse<StorefrontVariantRowResponse>` | `VariantId`, `EffectivePrice`, `StockAvailability`, `CanPurchase`, `Sku`, `PrimaryImageUrl`, `RatingAverage`, `RatingCount` |
| `GET /api/v1/store/products/{slug}` | `GetStorefrontProductBySlugHandler` | `StorefrontProductResponse` | `Variants: IReadOnlyList<StorefrontVariantResponse>` with `Attributes: IReadOnlyList<VariantAttributeValueResponse>` |

### Frontend Integration Guide

#### Variant Count per Product
```javascript
// From /store/products/variants response
const variantCountPerProduct = variantGridResponse.items.reduce((acc, row) => {
  acc[row.ProductId] = (acc[row.ProductId] || 0) + 1;
  return acc;
}, {});

// Render: "3 variants available" badge on product card
```

#### Concatenated Variant Attributes
```javascript
// From StorefrontProductResponse.Variants[n].Attributes
const formatVariantAttributes = (variant) => {
  if (!variant.Attributes || variant.Attributes.length === 0) return "";
  return variant.Attributes.map(a => a.Value).join(", ");
  // Example: ["Red", "Large", "128GB"] → "Red, Large, 128GB"
};

// Render on PDP variant selector
variant.Attributes?.forEach(attr => {
  // Display as pills: [Storage: 128GB] [Color: Red]
});
```

#### API Request Examples
```bash
# Variant grid (listing page)
GET /api/v1/store/products/variants?page=1&pageSize=20&sortBy=price&sortDirection=asc

# Single product detail (PDP)
GET /api/v1/store/products/iphone-15-pro
```

---

## 2. Product Details Page (PDP) Pre-selection

### Requirement
- Pre-select specific variant when navigating from storefront to PDP

### Gap Analysis: **FRONTEND-ONLY (URL ROUTING)**

### Implementation: URL Parameters
```javascript
// Storefront → PDP navigation
// From variant grid row click:
router.push(`/product/${productSlug}?variant=${variantId}&sku=${variantSku}`);

// PDP component on mount:
const { variant, sku } = useSearchParams();
useEffect(() => {
  if (variant) {
    const targetVariant = product.Variants.find(v => v.Id === variant);
    if (targetVariant) setSelectedVariant(targetVariant);
  }
}, [variant, product]);
```

### No Backend Changes
- Selection state is ephemeral UI state
- No persistence needed (stateless navigation)
- Existing `GET /store/products/{slug}` returns all variants for client-side matching

---

## 3. Cart Data Integrity

### Requirement
- `GET /cart` returns granular variant details for every line item

### Gap Analysis: **ALREADY IMPLEMENTED**

### Existing Response Structure (`CartItemResponse`)
```csharp
public sealed record CartItemResponse(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string ProductSlug,
    Guid? VariantId,                    // ✅ Present
    string? VariantDescription,         // ✅ "SKU: TSH-RED"
    string? Sku,                        // ✅ Variant SKU or Product SKU
    decimal UnitPrice,                  // ✅ Effective price (variant override or product)
    int Quantity,
    decimal LineTotal,
    string Currency,
    StockAvailability StockAvailability, // ✅ Per-variant stock
    bool CanPurchase,                   // ✅ Per-variant purchasability
    string? PrimaryImageUrl,
    IReadOnlyList<VariantAttributeValueResponse>? VariantAttributes = null // ✅ Resolved attributes
);
```

### Frontend Usage
```javascript
// Cart page rendering
cart.Items.forEach(item => {
  // Variant display name: "iPhone 15 Pro - Red, 128GB"
  const variantLabel = item.VariantAttributes 
    ? item.VariantAttributes.map(a => a.Value).join(", ")
    : item.VariantDescription || "Default";
  
  // Stock badge
  const stockBadge = item.CanPurchase 
    ? (item.StockAvailability === "LowStock" ? "⚠️ Low Stock" : "✅ In Stock")
    : "❌ Out of Stock";
  
  // Price display
  const priceDisplay = `${item.Currency} ${item.UnitPrice.toFixed(2)} × ${item.Quantity}`;
});
```

---

## 4. Order Summary Accuracy

### Requirement
- Order summary displays selected variant attributes for each item

### Gap Analysis: **ALREADY IMPLEMENTED**

### Existing Response Structure (`OrderItemResponse`)
```csharp
public sealed record OrderItemResponse(
    Guid Id,
    Guid ProductId,
    Guid? VariantId,                    // ✅ Present
    string ProductName,
    string? VariantDescription,         // ✅ Snapshot at checkout
    string? Sku,                        // ✅ Snapshot at checkout
    decimal UnitPrice,                  // ✅ Snapshot at checkout
    int Quantity,
    decimal LineTotal,
    string? PrimaryImageUrl,
    IReadOnlyList<VariantAttributeValueResponse>? VariantAttributes = null // ✅ Resolved attributes
);
```

### Key Implementation Detail
- **Variant attributes are resolved at order fetch time** (not snapshotted) via `GetMyOrderByIdHandler.BuildVariantAttributeMapAsync`
- This ensures attribute labels stay current even if admin renames attribute values
- Order items snapshot: `ProductName`, `VariantDescription`, `Sku`, `UnitPrice`

### Frontend Usage
```javascript
// Order detail page
order.Items.forEach(item => {
  const variantDetails = item.VariantAttributes?.map(a => 
    `${a.AttributeName}: ${a.Value}`
  ).join(", ") || item.VariantDescription || "Standard";
  
  // Render: "iPhone 15 Pro — Color: Red, Storage: 128GB"
  // Price shown is the EXACT price paid (immutable snapshot)
});
```

---

## 5. Cart/Order Line Item Logic (Grouping vs. Splitting)

### Requirement
- Same ProductID + VariantID → increment quantity
- Different VariantID → new line item

### Gap Analysis: **ALREADY IMPLEMENTED IN BACKEND**

### Existing Logic (`Cart.AddItem` in `Cart.cs:84-95`)
```csharp
public CartItem AddItem(Guid productId, Guid? variantId, int quantity)
{
    var existing = _items.FirstOrDefault(i =>
        i.ProductId == productId && i.VariantId == variantId);  // ✅ Groups by BOTH
    
    if (existing is not null)
    {
        existing.SetQuantity(existing.Quantity + quantity);  // ✅ Increments
        return existing;
    }
    
    var item = CartItem.Create(Id, productId, variantId, quantity);  // ✅ New line
    _items.Add(item);
    return item;
}
```

### Database Upsert (`AddCartItemHandler.cs:86-88`)
```csharp
await db.UpsertCartItemAsync(
    cartId, command.ProductId, command.VariantId,
    command.Quantity, cancellationToken);
```
- PostgreSQL `INSERT ON CONFLICT DO UPDATE Quantity +=` on unique constraint `(CartId, ProductId, VariantId)`

### Frontend Responsibility
```javascript
// Just send ProductId + VariantId + Quantity
// Backend handles grouping automatically
await api.post('/cart/items', {
  productId: "guid",
  variantId: "guid-or-null",  // null for products without variants
  quantity: 1
});
```

---

## 6. Order History (Admin & User Views)

### Requirement
- Both Admin and User order history display full variant details

### Gap Analysis: **ALREADY IMPLEMENTED**

### Shared Mapping Logic (`GetMyOrderByIdHandler.MapToResponse`)
Used by:
- `GetMyOrderByIdHandler` (Customer orders)
- `GetOrderByIdHandler` (Admin orders)  
- `UpdateOrderStatusHandler` (Admin)

### Data Included in Every Order Response
| Field | Source | Notes |
|-------|--------|-------|
| `VariantId` | OrderItem.VariantId | Snapshot |
| `VariantDescription` | OrderItem.VariantDescription | Snapshot (e.g., "SKU: TSH-RED") |
| `Sku` | OrderItem.Sku | Variant SKU or Product SKU |
| `VariantAttributes` | Resolved via `BuildVariantAttributeMapAsync` | Live lookup for current labels |
| `UnitPrice` | OrderItem.UnitPrice | Immutable snapshot |
| `LineTotal` | OrderItem.LineTotal | Immutable snapshot |

### Frontend Integration (Both Admin & User)
```javascript
// Same component reusable for both views
const OrderItemDisplay = ({ item }) => (
  <div className="order-item">
    <h4>{item.ProductName}</h4>
    {item.VariantAttributes && item.VariantAttributes.length > 0 && (
      <div className="variant-attrs">
        {item.VariantAttributes.map(attr => (
          <span key={attr.AttributeValueId} className="attr-pill">
            {attr.AttributeName}: {attr.Value}
          </span>
        ))}
      </div>
    )}
    <div className="price-qty">
      {item.UnitPrice} × {item.Quantity} = {item.LineTotal}
    </div>
  </div>
);
```

---

## Backend Modification Specification

### **No Mandatory Changes Required**

The following optional enhancements could improve developer experience:

| Enhancement | Effort | Impact |
|-------------|--------|--------|
| Add `VariantCount` to `StorefrontProductSummaryResponse` | Low (1 field + query) | Avoids client-side aggregation |
| Add `VariantAttributeNames` (CSV) to `StorefrontVariantRowResponse` | Low (1 field) | Quick display without resolving full objects |
| Add `GET /cart/variant-summary` endpoint | Medium | Pre-aggregated variant counts for cart badge |

### Migration: None Required

All schema already supports variants:
- `ProductVariants` table with `AttributeValueIds` CSV
- `CartItems` table with `(CartId, ProductId, VariantId)` unique index
- `OrderItems` table with `VariantId`, `VariantDescription`, `Sku`
- `InventoryItems` table with `(ProductId, VariantId)` unique index

---

## Frontend Integration Guide Summary

### API Endpoints Reference

| Feature | Endpoint | Method | Key Response Fields |
|---------|----------|--------|---------------------|
| Product Listing (with variants) | `/api/v1/store/products/variants` | GET | `items[]: StorefrontVariantRowResponse` |
| Product Detail (PDP) | `/api/v1/store/products/{slug}` | GET | `variants[]: StorefrontVariantResponse` |
| Cart | `/api/v1/cart` | GET | `items[]: CartItemResponse` |
| Add to Cart | `/api/v1/cart/items` | POST | `productId, variantId?, quantity` |
| User Orders List | `/api/v1/orders` | GET | `items[]: OrderSummaryResponse` |
| User Order Detail | `/api/v1/orders/{id}` | GET | `items[]: OrderItemResponse` |
| Admin Order Detail | `/api/v1/admin/orders/{id}` | GET | `items[]: OrderItemResponse` |

### State Management Recommendations

```javascript
// React/Vue/Svelte store pattern
const productStore = {
  // Variant grid (listing)
  variantGrid: { items: [], total: 0, page: 1 },
  
  // PDP - single product with all variants
  currentProduct: null,
  selectedVariantId: null,  // From URL param
  
  // Cart
  cart: { items: [], subtotal: 0, currency: "INR" },
  
  // Orders
  orders: { items: [], total: 0 },
  currentOrder: null,
  
  // Actions
  async fetchVariantGrid(filters) { /* ... */ },
  async fetchProduct(slug) { /* ... */ },
  async fetchCart() { /* ... */ },
  async addToCart(productId, variantId, qty) { /* ... */ },
  async fetchOrders() { /* ... */ },
  async fetchOrder(id) { /* ... */ },
  
  // Derived
  getVariantCount(productId) {
    return this.variantGrid.items.filter(i => i.ProductId === productId).length;
  },
  getSelectedVariant() {
    return this.currentProduct?.Variants?.find(v => v.Id === this.selectedVariantId);
  }
};
```

### Routing Implementation

```javascript
// Routes
/routes/
  store/                    → ProductListing (uses /variants endpoint)
  store/products/:slug      → ProductDetail (uses /{slug} endpoint)
  cart                      → CartPage (uses /cart endpoint)
  checkout                  → CheckoutPage
  orders                    → OrderHistory (uses /orders endpoint)
  orders/:id                → OrderDetail (uses /orders/{id} endpoint)
  admin/orders/:id          → AdminOrderDetail (uses /admin/orders/{id})

// PDP Pre-selection
// URL: /store/products/iphone-15?variant=abc-123&sku=IPH-RED-128
// On PDP mount: read URL params → find matching variant → set selected
```

---

## Testing Checklist

### Unit Tests (Existing)
- [x] `StorefrontVariantGridIntegrationTests` - 6 tests covering variant grid
- [x] `CartDomainTests` - Cart grouping logic
- [x] `OrderDomainTests` - Order item creation with variants

### Integration Tests Needed
- [ ] Cart add/update with variantId verifies grouping
- [ ] Order creation snapshots variant attributes correctly
- [ ] Order history resolves variant attributes for historical orders
- [ ] Variant grid pagination + sorting + filtering combinations

### E2E Scenarios
- [ ] Storefront → PDP (with variant param) → Cart → Checkout → Order History
- [ ] Multiple variants of same product in cart as separate lines
- [ ] Admin order view matches customer order view for variant details

---

## Conclusion

**All 6 requirements are satisfied by existing backend implementation.** The frontend team can proceed with integration using the documented endpoints and response structures. No backend schema changes, new endpoints, or logic modifications are required.

The variant system is fully implemented at the domain, application, and API layers with proper:
- Variant-to-attribute resolution
- Per-variant pricing (`GetEffectivePrice`)
- Per-variant inventory tracking
- Cart grouping by `(ProductId, VariantId)`
- Order snapshotting with live attribute resolution
- Storefront variant grid for listing pages