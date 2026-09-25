# Requirements Traceability

## Customer storefront
- Public homepage
- Product catalog
- Product details
- Search/filter/sort
- Cart
- Checkout
- Login
- Google auth
- Mobile OTP
- Orders
- Order tracking
- Policies/contact

## Commerce
- Product catalog
- Variants
- Pricing
- Stock
- Tax
- Coupons
- Promotions
- Delivery
- Expected delivery
- Razorpay
- COD
- Refunds
- Cancellation/rejection
- Returns

## Shipping
- Admin tracking number
- Automatic provider
- Manual fallback
- Normalized tracking timeline
- SignalR updates
- No frontend polling

## Communications
- Brevo managed
- Customer Brevo
- Generic code-owned templates
- Configurable business logo/details
- SMS provider selection
- OTP

## Configurability
Business-specific values must be Admin configurable without code:
- Branding
- Business details
- Catalog
- Prices
- Stock
- Promotions
- Coupons
- Delivery
- Currency
- Country
- Timezone
- Policies
- Navigation
- Homepage content
- Integrations

## Explicitly NOT configurable
- Frontend layout/theme structure
- Core business logic
- Order state machine
- Security model
- Database schema
- Application architecture
- Provider implementation code
- Email HTML templates in V1
