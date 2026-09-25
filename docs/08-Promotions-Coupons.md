# Coupons and Promotions

## Coupons
Support:
- Percentage discount
- Fixed discount
- Free shipping
- Minimum order amount
- Maximum discount
- Start/end date
- Global usage limit
- Per-customer usage limit
- Applicable products
- Applicable categories
- Active/inactive

## Promotions
V1:
- Buy X Get Y
- Quantity-based discount
- Product discount
- Category discount
- Free shipping

## Rules
- Server-side validation
- Deterministic calculation
- Prevent stacking unless explicitly supported
- Define precedence when multiple discounts apply
- Persist discount snapshots on order

## Security
Never accept final discount/total from frontend.

## Testing
Cover combinations:
- Coupon + promotion
- Coupon + free shipping
- Buy X Get Y + tax
- Expired coupon
- Usage limit
- Concurrent coupon usage
