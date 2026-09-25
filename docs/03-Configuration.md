# Configuration

The customer configures business behavior through Admin. Infrastructure secrets may be deployment-level secrets.

## Business
- Business name
- Legal name
- Logo
- Favicon
- Website URL
- Support email
- Phone
- Address
- GST/tax identifiers where applicable
- Country
- Currency
- Timezone
- Culture
- Social links

## Storefront content
- Homepage section content and visibility
- Hero/banner
- Featured products/categories
- Navigation
- Footer
- Static pages
- SEO metadata

Theme/layout remains code-controlled and static.

## Catalog
- Categories
- Brands
- Products
- Variants
- Attributes
- Images
- Prices
- Stock
- Tax
- Product-specific delivery settings

## Commerce
- Delivery charge
- Free-shipping threshold
- Processing days
- Delivery days
- COD enable/disable
- Payment methods
- Coupon settings
- Promotion settings
- Store open/closed status

## Authentication
- Google OAuth enabled/disabled
- Email/password enabled/disabled
- Mobile OTP enabled/disabled
- OTP expiry
- OTP resend cooldown
- OTP attempt limit
- SMS provider

## Email
Two modes:
1. Kromic Managed Brevo
2. Customer Brevo

Email templates remain in code. Admin configures logo/business information and sender/provider settings.

## Payments
Razorpay credentials and webhook secret are deployment/customer secrets.

## Shipping
- Tracking provider
- Automatic/manual tracking
- Provider credentials
- Delivery configuration

## Media
Cloudinary is the standard asset provider. Customers do not need to select a storage provider in V1.

## Cache
IMemoryCache only in V1. Cache settings may be internal configuration, not customer business configuration.

## Operating status
Admin can:
- Open/close store
- Set temporary closure
- Disable COD
- Disable a payment method
- Disable a product/category

## Secrets
Never expose API secrets through public APIs. Store secrets securely in environment/secret storage.
