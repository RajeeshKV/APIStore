# Domain Model

## Core aggregates/entities
- User
- CustomerProfile
- Address
- Product
- ProductVariant
- Category
- Brand
- ProductImage
- InventoryItem
- InventoryReservation
- Cart
- CartItem
- Order
- OrderItem
- Payment
- Refund
- Shipment
- ShipmentTrackingEvent
- Coupon
- Promotion
- PromotionRule
- BusinessSettings
- StoreSettings
- HomepageSection
- NavigationItem
- StaticPage
- MediaAsset
- RefreshToken
- OtpRequest
- OutboxEvent
- WebhookEvent / WebhookDelivery record
- AuditLog

## Order status
Persist enum names as strings:
- PendingPayment
- Confirmed
- Processing
- Packed
- Shipped
- OutForDelivery
- Delivered
- Cancelled
- Rejected
- ReturnRequested
- Returned
- RefundPending
- Refunded

Only valid state transitions are allowed.

## Shipment status
- Pending
- Shipped
- InTransit
- OutForDelivery
- Delivered
- Exception
- Returned

Provider-specific statuses are normalized into internal statuses.

## Money
- Use decimal.
- Never use double/float for monetary values.
- Store CurrencyCode as ISO 4217, e.g. INR.
- Define explicit decimal precision in EF configuration.

## Time
- Store canonical timestamps in UTC.
- Store CountryCode, CurrencyCode, TimeZoneId, and Culture separately.
- Country may suggest defaults but must not force timezone/currency.

## Soft delete
Use only where business history requires it. Do not add soft-delete fields to every entity by default.

## Concurrency
Inventory and other transactional resources require concurrency protection. Orders must not oversell stock.

## Domain rules
Examples:
- Product must have valid pricing.
- Variant SKU must be unique where applicable.
- Order totals are calculated server-side.
- Coupon applicability is validated server-side.
- Stock is validated/reserved transactionally.
- Refund cannot exceed captured amount.
- Invalid order-state transitions are rejected.
