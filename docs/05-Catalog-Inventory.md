# Catalog and Inventory

## Product
Fields include:
- Name
- Slug
- SKU
- Description
- Short description
- Price
- Compare-at price
- Tax configuration
- Status
- Brand
- Category
- Attributes
- Variants
- Images
- SEO fields
- Delivery configuration

## Catalog features
Customer:
- Search
- Category filtering
- Attribute filtering
- Price filtering
- Sorting
- Pagination
- Availability
- Variant selection

## Images
Cloudinary stores images/assets. PostgreSQL stores public ID, URL/derived metadata, alt text, sort order, and relationships.

## Inventory
Track:
- OnHand
- Reserved
- Available
- Low-stock threshold

## Reservation
At checkout/payment:
- Validate stock
- Reserve stock
- Associate reservation with order/cart
- Expire/release reservation when payment fails or reservation expires
- Finalize reservation when payment succeeds

## Concurrency
Use database transaction/concurrency controls to prevent overselling.

## Admin
Admin can create/update/archive products, variants, stock, categories, brands, images, and availability.

## Cache
Cache product reads and catalog configuration. Invalidate relevant cache entries after successful mutations. Database remains source of truth.
