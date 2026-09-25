# Implementation Plan

Implement in batches. Each batch must compile, pass relevant tests, and leave the application runnable.

## Phase 0 — Foundation
- Solution/projects
- Clean dependency boundaries
- Global usings
- Result/error model
- Base entity/auditing
- Serilog
- Global exception middleware
- API versioning
- Swagger/OpenAPI
- DI extension methods
- Clean Program.cs
- Configuration/options
- Health endpoint
- PostgreSQL/EF Core
- Enum string conversion
- Base migrations

## Phase 1 — Identity
- User/customer model
- Password auth
- JWT
- Refresh tokens
- Rotation
- Token versioning
- Google OAuth
- Roles/policies
- OTP abstraction
- SMS providers
- Rate limiting

## Phase 2 — Business/Store Configuration
- Business settings
- Country/currency/timezone/culture
- Store open/closed
- Static pages
- Navigation
- Homepage configuration
- SEO
- Cloudinary integration

## Phase 3 — Catalog
- Categories
- Brands
- Products
- Variants
- Attributes
- Images
- Inventory
- Product queries
- Admin CRUD
- Caching/invalidation

## Phase 4 — Cart/Checkout
- Cart
- Address
- Delivery calculation
- Tax
- Stock reservation
- Checkout validation
- Guest checkout
- Order creation

## Phase 5 — Payments
- Razorpay
- Payment verification
- Webhooks
- Idempotency
- Refunds
- COD

## Phase 6 — Promotions
- Coupons
- Promotions
- Buy X Get Y
- Quantity discounts
- Free shipping
- Usage limits

## Phase 7 — Shipping
- Shipment
- Tracking provider abstraction
- Automatic tracking
- Manual fallback
- Tracking events
- Tracking webhook
- Expected delivery

## Phase 8 — Email/Notifications
- Generic code-owned email templates
- Kromic Brevo
- Customer Brevo
- Outbox
- Background email worker
- SignalR notifications

## Phase 9 — Admin
- Dashboard
- Products
- Inventory
- Orders
- Customers
- Coupons
- Promotions
- Delivery
- Payments/refunds
- Shipping
- Store settings
- Integrations
- Audit

## Phase 10 — Hardening
- Full test suite
- Concurrency tests
- Idempotency tests
- Security review
- Rate-limit review
- Performance testing
- Cache review
- Database index review
- Error handling review
- Backup/restore test

## Phase 11 — Production readiness
- Docker
- CI/CD
- Production secrets
- Migration strategy
- Health checks
- Monitoring/logging
- Backup
- Domain/SSL
- Smoke tests
- Onboarding documentation

## Definition of done per phase
- Production projects compile
- Relevant tests pass
- Database migration is valid
- No placeholder TODOs in completed scope
- API documented
- Authorization implemented
- Logging/error handling included
- No secrets committed
- Frontend/backend contracts are synchronized
