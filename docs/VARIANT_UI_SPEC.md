# Variant-Aware Frontend Implementation Guide

> **Non-negotiable rules — follow exactly. Every rule is verified against the backend contract.**

---

## 0. GLOBAL RULES (APPLY TO ALL SCREENS)

| Rule | Description |
|------|-------------|
| **G1** | Every product display MUST feature the primary product image (`isPrimary: true`). If none marked primary, use `sortOrder: 0`. |
| **G2** | **Routing rule:** Clicking a product card/name → `/products/{productSlug}`. Clicking a specific variant (size/color swatch on listing, or variant selector on PDP) → `/products/{productSlug}?variant={variantId}` (or equivalent deep-link state). |
| **G3** | **Image source hierarchy:** `resolvedVariant?.images` (when variant selected) → `product.images` (when no variant selected OR no variants exist). **Never mix.** |
| **G4** | **Price source hierarchy:** `resolvedVariant?.effectivePrice` → `product.price`. |
| **G5** | **Stock source hierarchy:** `resolvedVariant?.stockAvailability` → `product.stockAvailability` (rolled up). |
| **G6** | **Add to Cart payload:** When `product.variants.length > 0`, ALWAYS send `variantId` (never null). When no variants, send `variantId: null`. |
| **G7** | **Attribute resolution:** Variant match is EXACT set match on `attributeValueIds` (CSV → sorted array). No partial matches. |
| **G8** | **Disabled swatches:** A value is unavailable if NO active+purchasable variant contains it given other selections. |
| **G9** | Currency formatting ALWAYS uses `product.currency`. |
| **G10** | Placeholder image: `/placeholder-product.png` when no images exist at any level. |

---

## 1. PRODUCT LISTING (STOREFRONT)

### 1.1 Data Contract (`GET /api/v1/store/products`)

```typescript
interface StorefrontProductListItem {
  id: string;
  name: string;
  slug: string;
  price: number;                    // Base price (when NO variant selected)
  compareAtPrice: number | null;
  currency: string;
  stockAvailability: 'InStock' | 'LowStock' | 'OutOfStock';  // Rolled up
  canPurchase: boolean;             // True if ANY variant purchasable
  primaryImage: StorefrontImage | null;  // Product-level primary (variantId === null)
  hasVariants: boolean;             // product.variants.length > 0
  variantCount: number;             // Number of active variants
  categoryId: string;
  categoryName: string;
  brandId: string;
  brandName: string;
  isFeatured: boolean;
  ratingAverage: number;
  ratingCount: number;
}

interface StorefrontImage {
  id: string;
  url: string;
  altText: string | null;
  sortOrder: number;
  isPrimary: boolean;
}
```

### 1.2 Product Card Component

```
ProductCard
├── Link → /products/{slug}
│   └── ImageWrapper
│       └── PrimaryImage (primaryImage?.url ?? /placeholder-product.png)
│       └── BadgeOverlay (if hasVariants): "{variantCount} options"
├── ProductInfo
│   ├── CategoryLink → /category/{categorySlug}
│   ├── ProductNameLink → /products/{slug}
│   ├── BrandLink → /brand/{brandSlug}
│   ├── RatingDisplay (ratingAverage, ratingCount)
│   └── PriceDisplay
│       ├── CurrentPrice (product.price, formatted with currency)
│       └── CompareAtPrice (if > price)
│   └── VariantSwatchStrip (ONLY if hasVariants)
│       └── SwatchButton[] (first 3-4 variant attributes, e.g., colors)
│           └── OnClick: navigate to /products/{slug}?variant={variantId}
│   └── StockBadge (product.stockAvailability)
└── QuickAddButton (optional)
    ├── Disabled: !canPurchase
    ├── OnClick: if hasVariants → open QuickSelect modal, else → add to cart directly
```

### 1.3 Variant Handling on Listing

| Scenario | Image | Price | Swatches | CTA |
|----------|-------|-------|----------|-----|
| **No variants** (`hasVariants: false`) | `primaryImage` (product-level) | `price` | None | "Add to Cart" (sends `variantId: null`) |
| **Has variants** (`hasVariants: true`) | `primaryImage` (product-level) | `price` (base) | Show first N variant attribute values (e.g., colors) as clickable swatches | "Select options" OR QuickSelect modal |

### 1.4 QuickSelect Modal (Optional Enhancement)

- Triggered from listing "Quick Add" on variant products
- Shows compact variant selector (same logic as PDP but inline)
- Resolves variant → adds to cart with `variantId` → closes
- Must respect disabled swatches (`isValueAvailable`)

### 1.5 Listing Edge Cases

| Edge Case | Handling |
|-----------|----------|
| No primary image, no images at all | Show placeholder |
| Product has variants but NO active variants | Treat as simple product (hide swatches, use base price) |
| `compareAtPrice` <= `price` | Hide compare-at |
| `ratingCount === 0` | Hide rating or show "No reviews" |
| Variant swatch click → deep link | Update URL without full page reload if using SPA routing |

---

## 2. PRODUCT DETAIL PAGE (PDP)

*See existing spec (sections 1-11) for complete PDP implementation. Key rules reiterated:*

### 2.1 Auto-Select Default Variant on Load

```typescript
function useDefaultVariant(product: StorefrontProductResponse) {
  const [selection, setSelection] = useState<Record<string, string>>({});
  
  useEffect(() => {
    if (product.variants.length === 0) return;
    
    // Default: first active + purchasable variant (by sortOrder)
    const defaultVariant = product.variants
      .filter(v => v.isActive && v.canPurchase)
      .sort((a, b) => a.sortOrder - b.sortOrder)[0];
    
    if (defaultVariant) {
      const ids = (defaultVariant.attributeValueIds ?? '')
        .split(',')
        .map(s => s.trim())
        .filter(Boolean);
      // Map to selection state: { attributeId: valueId }
      // Requires reverse lookup from attributeValueId → attributeId
      const defaultSelection: Record<string, string> = {};
      ids.forEach(valueId => {
        const attr = defaultVariant.attributes.find(a => a.attributeValueId === valueId);
        if (attr) defaultSelection[attr.attributeId] = valueId;
      });
      setSelection(defaultSelection);
    }
  }, [product.variants]);
  
  return selection;
}
```

### 2.2 Image Gallery Dynamic Update

```typescript
function Gallery({ product, resolvedVariant }: GalleryProps) {
  // G1, G3: Strict hierarchy
  const images = resolvedVariant?.images?.length > 0
    ? resolvedVariant.images
    : product.images;
  
  const hero = images.find(i => i.isPrimary) ?? images[0] ?? null;
  const thumbnails = images.slice(0, 6);
  
  return (
    <div className="gallery">
      <HeroImage src={hero?.url ?? '/placeholder-product.png'} alt={hero?.altText} />
      <ThumbnailStrip 
        images={thumbnails}
        activeSrc={hero?.url}
        onSelect={src => setHeroSrc(src)}
      />
    </div>
  );
}
```

### 2.3 URL State Sync

- On variant resolution: `history.replaceState(null, '', \`?variant=${resolved.id}\`)`
- On page load with `?variant=xxx`: pre-select that variant in selector
- On variant change: update URL, scroll gallery to top

---

## 3. CART SLIDE-IN (MINI-CART)

### 3.1 Data Contract (`GET /api/v1/cart`)

```typescript
interface CartResponse {
  id: string;
  items: CartItem[];
  subtotal: number;
  itemCount: number;
}

interface CartItem {
  id: string;                    // CartItem id (not product id)
  productId: string;
  productName: string;
  productSlug: string;
  variantId: string | null;      // NULL only for simple products
  variantSku: string | null;
  quantity: number;
  unitPrice: number;             // Variant.effectivePrice OR product.price
  totalPrice: number;            // unitPrice * quantity
  currency: string;
  image: StorefrontImage | null; // Variant image (if variant) OR product primary
  variantAttributes: VariantAttribute[]; // Resolved attributes for display
  stockAvailability: 'InStock' | 'LowStock' | 'OutOfStock';
  canPurchase: boolean;
}
```

### 3.2 Mini-Cart Item Component

```
CartItem (slide-in row)
├── Link → /products/{productSlug}{variantId ? '?variant=' + variantId : ''}
│   └── Thumbnail (item.image?.url ?? /placeholder-product.png)
├── ItemInfo
│   ├── ProductNameLink → /products/{slug}
│   ├── VariantAttributeChips (if item.variantAttributes.length > 0)
│   │   └── Chip[]: "{attributeName}: {value}" (e.g., "Color: Red", "Size: M")
│   ├── PriceLine
│   │   ├── UnitPrice (item.unitPrice)
│   │   └── TotalPrice (item.totalPrice, emphasized)
│   └── StockBadge (item.stockAvailability)
├── QuantitySelector
│   ├── DecrementButton (disabled at 1)
│   ├── QuantityInput (controlled, min=1, max=stock/99)
│   └── IncrementButton (disabled if would exceed stock)
└── RemoveButton
    └── OnClick: DELETE /api/v1/cart/items/{item.id}
```

### 3.3 Variant Display Rules for Mini-Cart

| Field | Source |
|-------|--------|
| Image | `item.image` (variant-specific if exists, else product primary) |
| Price | `item.unitPrice` (already resolved to variant price) |
| Attributes | `item.variantAttributes` — render as chips: "Color: Red, Size: M" |
| Stock | `item.stockAvailability` (variant-level) |
| Quantity max | Backend enforces; frontend shows max if known |

### 3.4 Mini-Cart Edge Cases

| Edge Case | Handling |
|-----------|----------|
| Variant deleted after added to cart | Backend returns item with `canPurchase: false`, show "Unavailable — remove" |
| Price changed after added to cart | Mini-cart shows `item.unitPrice` (captured at add time); refresh on reopen |
| Quantity > available stock | Disable increment, show "Only X left" |
| Simple product (no variant) | No variant chips; link has no `?variant=` |

---

## 4. ORDER SUMMARY (CHECKOUT)

### 4.1 Data Contract (`GET /api/v1/checkout/summary` or cart at checkout step)

```typescript
interface CheckoutSummaryItem {
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
}

interface CheckoutSummary {
  items: CheckoutSummaryItem[];
  subtotal: number;
  shipping: number;
  discount: number;
  tax: number;
  total: number;
  currency: string;
}
```

### 4.2 Order Summary Item Component

```
OrderSummaryItem
├── Thumbnail (item.image?.url ?? /placeholder-product.png)
├── ItemDetails
│   ├── ProductNameLink → /products/{slug}
│   ├── VariantAttributeList (if item.variantAttributes.length > 0)
│   │   └── Row[]: "{attributeName}: {value}"
│   │       (vertical list, not chips — more readable in summary)
│   ├── SKU (if item.variantSku): "SKU: {variantSku}"
│   └── Quantity: "Qty: {quantity}"
├── LinePrice
│   └── TotalPrice (item.totalPrice, right-aligned)
└── StockIndicator (subtle): {stockAvailability}
```

### 4.3 Variant Display Rules for Checkout

- **Always show variant attributes** in a vertical list (not horizontal chips) for readability
- **Include SKU** if available (`variantSku`) — helps customer verify correct item
- **Price shown is final line total** (`unitPrice * quantity`), no per-unit breakdown needed
- **Link to product** includes `?variant=` for variant items

### 4.4 Checkout Edge Cases

| Edge Case | Handling |
|-----------|----------|
| Variant price differs from when added | Summary shows captured `unitPrice`; payment uses captured price |
| Variant went out of stock | Block checkout step, show "Item unavailable — update cart" |
| Multiple quantities of same variant | Single line with `Qty: X`, line total = `unitPrice * X` |

---

## 5. ORDER HISTORY

### 5.1 Data Contract (`GET /api/v1/orders` and `GET /api/v1/orders/{id}`)

```typescript
interface OrderHistoryItem {
  id: string;
  orderNumber: string;
  status: string;
  placedAt: string;              // ISO datetime
  total: number;
  currency: string;
  itemCount: number;
  items: OrderHistoryLineItem[];
}

interface OrderHistoryLineItem {
  id: string;                    // OrderItem id
  productId: string;
  productName: string;
  productSlug: string;
  variantId: string | null;
  variantSku: string | null;
  quantity: number;
  unitPrice: number;             // Price AT TIME OF ORDER (immutable)
  totalPrice: number;
  currency: string;
  image: StorefrontImage | null; // Image AT TIME OF ORDER
  variantAttributes: VariantAttribute[]; // Frozen at order time
  productStatus: 'Active' | 'Archived' | 'Deleted'; // Current product status
}
```

### 5.2 Order History Item Component

```
OrderHistoryCard (per order)
├── OrderHeader
│   ├── OrderNumber: #{orderNumber}
│   ├── PlacedAt (formatted date)
│   ├── StatusBadge (status)
│   └── Total (formatted)
├── OrderItemsList
│   └── OrderHistoryLineItem[] (max 3 visible, "+N more" link)
│       └── OrderHistoryLineItem
│           ├── Thumbnail (item.image?.url ?? /placeholder-product.png)
│           ├── LineDetails
│           │   ├── ProductNameLink → /products/{slug}{variantId ? '?variant=' + variantId : ''}
│           │   │   └── If productStatus !== 'Active': show "(No longer available)"
│           │   ├── VariantAttributeList (vertical)
│           │   │   └── Row[]: "{attributeName}: {value}"
│           │   ├── SKU (if variantSku): "SKU: {variantSku}"
│           │   └── Quantity: "Qty: {quantity}"
│           └── LinePrice
│               └── TotalPrice (item.totalPrice)
└── OrderActions
    ├── ViewDetails → /orders/{id}
    ├── Reorder (if all items purchasable)
    └── Return (if eligible)
```

### 5.3 Variant Display Rules for Order History

- **Frozen data:** All prices, images, attributes are captured at order time — do not re-fetch current product data
- **Product status indicator:** If product is archived/deleted, show "(No longer available)" next to name
- **Reorder logic:** Only enabled if ALL items have `productStatus: 'Active'` AND variants still purchasable
- **Link includes variant deep-link** so customer lands on exact variant they ordered

### 5.4 Order History Edge Cases

| Edge Case | Handling |
|-----------|----------|
| Product deleted after order | Show name with "(No longer available)", disable reorder, image may be placeholder |
| Variant deleted but product active | Show attributes as ordered, note "This specific option is no longer available" |
| Price changed since order | Show original `unitPrice` and `totalPrice` — never current price |
| Image changed since order | Show captured `image` from order, not current product image |

---

## 6. NAVIGATION & INTERACTION LOGIC (GLOBAL)

### 6.1 Click Behavior Matrix

| Click Target | Context | Navigation |
|--------------|---------|------------|
| Product card image | Listing | `/products/{slug}` |
| Product card name | Listing | `/products/{slug}` |
| Product card variant swatch | Listing | `/products/{slug}?variant={variantId}` |
| "Quick Add" button (simple product) | Listing | Add to cart (no navigation) |
| "Quick Add" button (variant product) | Listing | Open QuickSelect modal |
| Variant swatch in QuickSelect | Modal | Resolve variant → add to cart with `variantId` |
| Product name on PDP | PDP | No navigation (already on PDP) |
| Variant selector value | PDP | Update URL `?variant={variantId}`, update gallery/price |
| Thumbnail in gallery | PDP | Update hero image (same gallery source) |
| Mini-cart item thumbnail | Mini-cart | `/products/{slug}{?variant=}` |
| Mini-cart item name | Mini-cart | `/products/{slug}{?variant=}` |
| Order summary item name | Checkout | `/products/{slug}{?variant=}` (opens in new tab recommended) |
| Order history item name | History | `/products/{slug}{?variant=}` |
| Reorder button | History | Adds to cart with captured `variantId`, opens mini-cart |

### 6.2 Deep Link Handling on PDP Load

```typescript
function useDeepLinkVariant(product: StorefrontProductResponse) {
  const [selection, setSelection] = useState<Record<string, string>>({});
  
  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    const variantId = params.get('variant');
    
    if (variantId && product.variants.length > 0) {
      const targetVariant = product.variants.find(v => v.id === variantId);
      if (targetVariant && targetVariant.isActive) {
        const ids = (targetVariant.attributeValueIds ?? '')
          .split(',')
          .map(s => s.trim())
          .filter(Boolean);
        const deepLinkSelection: Record<string, string> = {};
        ids.forEach(valueId => {
          const attr = targetVariant.attributes.find(a => a.attributeValueId === valueId);
          if (attr) deepLinkSelection[attr.attributeId] = valueId;
        });
        setSelection(deepLinkSelection);
      }
    }
  }, [product.variants]);
  
  return selection;
}
```

### 6.3 Image Loading Strategy

| Screen | Strategy |
|--------|----------|
| Listing | Lazy load `primaryImage` with `loading="lazy"`, `width`/`height` attrs for CLS prevention |
| PDP Hero | Priority load (`fetchpriority="high"`, no lazy), LCP candidate |
| PDP Thumbnails | Lazy load, preload next/prev on hover |
| Mini-cart | Eager load (small, few items) |
| Order Summary | Eager load |
| Order History | Lazy load |

---

## 7. STATE MANAGEMENT SUMMARY

### 7.1 PDP Variant Selector State

```typescript
// Local to PDP — NOT global
const [selection, setSelection] = useState<Record<string, string>>({});
const resolved = useMemo(() => resolveVariant(product, selection), [product, selection]);
const isFullySelected = resolved !== null;
const heroImage = resolved?.images?.find(i => i.isPrimary) ?? resolved?.images?.[0] 
  ?? product.images?.find(i => i.isPrimary) ?? product.images?.[0] ?? null;
```

### 7.2 Cart State (Global — Context/Redux/Zustand)

```typescript
interface CartState {
  items: CartItem[];
  itemCount: number;
  subtotal: number;
  isOpen: boolean;
  
  addItem: (productId: string, variantId: string | null, quantity: number) => Promise<void>;
  updateQuantity: (itemId: string, quantity: number) => Promise<void>;
  removeItem: (itemId: string) => Promise<void>;
  open: () => void;
  close: () => void;
}
```

### 7.3 Order History State (Server-fetched, cached)

- Fetched on demand per page
- Cached by order ID for detail view
- No client-side mutation (read-only)

---

## 8. EDGE CASES & PERFORMANCE IMPACTS

### 8.1 Performance

| Screen | Risk | Mitigation |
|--------|------|------------|
| Listing | N+1 image loads | Use `srcset`/`sizes`, lazy load, CDN |
| PDP | Gallery re-render on variant switch | Memoize `galleryImages`, `heroImage`; only swap `src` |
| Mini-cart | Frequent open/close | Keep cart state in memory; don't refetch on open |
| Order History | Large order lists | Paginate (20/page), virtualize if needed |

### 8.2 Usability Edge Cases

| Scenario | Impact | Handling |
|----------|--------|----------|
| User opens PDP with invalid `?variant=` | Shows base product | Ignore invalid, fall back to default variant logic |
| User shares PDP URL with variant | Recipient sees that variant | Deep-link logic handles on load |
| Cart has item whose variant was deleted | Confusing UX | Show "Unavailable" badge, disable quantity change, allow remove |
| Product has 50+ variants | Selector unusable | Group by attribute type; consider dropdown for >10 values per attribute |
| Mobile: swatch strip overflow | Horizontal scroll hidden | Scroll-snap container, visual scroll hint |

### 8.3 Accessibility

- All swatches: `role="radio"`, `aria-checked`, `aria-label="{attributeName}: {value}"`
- Disabled swatches: `aria-disabled="true"`, not removed from DOM
- Gallery: `aria-live="polite"` on hero change, keyboard navigation for thumbnails
- Price changes: announce to screen readers via `aria-live`
- Mini-cart: focus trap when open, `Escape` closes, focus returns to trigger

---

## 9. IMPLEMENTATION CHECKLIST (ALL SCREENS)

### Product Listing
- [ ] Primary image on every card (G1)
- [ ] Variant swatches link to `?variant=` (G2)
- [ ] Base price shown when variants exist (G4)
- [ ] Rolled-up stock badge (G5)
- [ ] QuickSelect modal sends `variantId` (G6)

### PDP
- [ ] Default variant auto-selected on load
- [ ] Gallery switches to variant images ONLY (G3, R1)
- [ ] Price switches to `effectivePrice` (G4)
- [ ] Stock badge switches (G5)
- [ ] Disabled swatches computed correctly (G8)
- [ ] URL syncs with variant selection (G2)
- [ ] Add to cart sends `variantId` (G6, R8)

### Mini-Cart
- [ ] Variant attributes shown as chips
- [ ] Variant image used (not product)
- [ ] Line price = `unitPrice * quantity`
- [ ] Link includes `?variant=`
- [ ] Unavailable items marked

### Order Summary
- [ ] Variant attributes vertical list
- [ ] SKU displayed if present
- [ ] Frozen prices (no re-fetch)
- [ ] Link includes `?variant=`

### Order History
- [ ] Frozen data (prices, images, attributes)
- [ ] Product status indicator
- [ ] Reorder respects variant availability
- [ ] Link includes `?variant=`

---

## 10. TYPE DEFINITIONS (CONSOLIDATED)

```typescript
// Shared
type StockAvailability = 'InStock' | 'LowStock' | 'OutOfStock';
type Currency = string;  // ISO 4217

interface StorefrontImage {
  id: string;
  url: string;
  altText: string | null;
  sortOrder: number;
  isPrimary: boolean;
}

interface AttributeValue {
  id: string;
  value: string;
  sortOrder: number;
}

interface ProductAttribute {
  id: string;
  name: string;
  sortOrder: number;
  values: AttributeValue[];
}

interface VariantAttribute {
  attributeValueId: string;
  attributeId: string;
  attributeName: string;
  value: string;
}

// Listing
interface StorefrontProductListItem {
  id: string;
  name: string;
  slug: string;
  price: number;
  compareAtPrice: number | null;
  currency: Currency;
  stockAvailability: StockAvailability;
  canPurchase: boolean;
  primaryImage: StorefrontImage | null;
  hasVariants: boolean;
  variantCount: number;
  categoryId: string;
  categoryName: string;
  categorySlug: string;
  brandId: string;
  brandName: string;
  brandSlug: string;
  isFeatured: boolean;
  ratingAverage: number;
  ratingCount: number;
}

// PDP
interface StorefrontVariant {
  id: string;
  sku: string | null;
  effectivePrice: number;
  sortOrder: number;
  isActive: boolean;
  attributeValueIds: string | null;
  stockAvailability: StockAvailability;
  canPurchase: boolean;
  attributes: VariantAttribute[];
  images: StorefrontImage[];
}

interface StorefrontProductResponse {
  id: string;
  name: string;
  slug: string;
  price: number;
  compareAtPrice: number | null;
  description: string | null;
  shortDescription: string | null;
  currency: Currency;
  stockAvailability: StockAvailability;
  canPurchase: boolean;
  images: StorefrontImage[];
  attributes: ProductAttribute[];
  variants: StorefrontVariant[];
  deliveryEstimate: {
    minDays: number;
    maxDays: number;
    processingDays: number;
  } | null;
}

// Cart
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
  currency: Currency;
  image: StorefrontImage | null;
  variantAttributes: VariantAttribute[];
  stockAvailability: StockAvailability;
  canPurchase: boolean;
}

// Checkout Summary
interface CheckoutSummaryItem {
  productId: string;
  productName: string;
  productSlug: string;
  variantId: string | null;
  variantSku: string | null;
  quantity: number;
  unitPrice: number;
  totalPrice: number;
  currency: Currency;
  image: StorefrontImage | null;
  variantAttributes: VariantAttribute[];
  stockAvailability: StockAvailability;
}

// Order History
interface OrderHistoryLineItem {
  id: string;
  productId: string;
  productName: string;
  productSlug: string;
  variantId: string | null;
  variantSku: string | null;
  quantity: number;
  unitPrice: number;
  totalPrice: number;
  currency: Currency;
  image: StorefrontImage | null;
  variantAttributes: VariantAttribute[];
  productStatus: 'Active' | 'Archived' | 'Deleted';
}
```

---

## 11. FINAL VERIFICATION

Before shipping any screen, verify:

- [ ] **G1**: Primary image visible on every product display
- [ ] **G2**: Variant clicks → deep link with `?variant=`
- [ ] **G3**: Image source never mixes variant + product
- [ ] **G4**: Price from variant when selected, else base
- [ ] **G5**: Stock from variant when selected, else rolled-up
- [ ] **G6**: Cart payload includes `variantId` when variants exist
- [ ] **G7**: Exact set match on attributes
- [ ] **G8**: Impossible swatches disabled
- [ ] **G9**: Currency from product
- [ ] **G10**: Placeholder when no images

**Backend contract is fixed. Frontend must match exactly.**

---

*This guide covers all 5 screens with consistent variant handling. The PDP section references the detailed spec in sections 1-11 of this document for implementation specifics.*