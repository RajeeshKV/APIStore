# Cart, Checkout and Orders

## Cart
- Add item
- Update quantity
- Remove item
- Variant selection
- Coupon
- Promotion
- Delivery calculation
- Tax
- Final total

Server recalculates all monetary values.

## Checkout
Validate:
- Customer
- Address
- Product availability
- Stock
- Price
- Coupon
- Promotion
- Delivery
- Tax
- Payment method

Never trust totals sent by the client.

## Guest checkout
May be enabled. Customer must provide required contact/address information and complete required mobile verification.

## Order creation
Use transactional logic for:
- Order
- Order items
- Price snapshots
- Stock reservation
- Payment initiation data
- Outbox events

## Order data
Snapshot product name, SKU, price, tax, discount, delivery charge, and currency at order time so historical orders remain accurate after catalog changes.

## Cancellation
Define allowed cancellation states and refund behavior.

## Rejection
Admin may reject an order for stock/unavailability or other configured reason. If payment was captured:
- Release stock
- Initiate refund
- Persist refund state
- Notify customer

## Returns
V1:
- Customer can request return
- Admin approves/rejects
- Admin processes refund
- Reverse logistics can remain manual

## Notifications
Use Outbox for email and SignalR events.
