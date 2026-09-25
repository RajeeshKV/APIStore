# Kromic Commerce — Master Specification

## Purpose
A reusable, deployable e-commerce platform for small businesses. Each customer receives an isolated deployment with its own PostgreSQL database, API, and storefront. The storefront structure/theme is static; business data and operational settings are configurable through Admin.

## Core principles
- .NET 8 backend
- React + Vite frontend
- PostgreSQL + EF Core
- Clean Architecture
- CQRS + Mediator
- DTOs in a separate Contracts project
- JWT access tokens + refresh-token rotation + token versioning
- Google OAuth
- IMemoryCache; no Redis in V1
- SignalR for real-time updates; API/database remains source of truth
- Outbox/background processing where asynchronous reliability is required
- Provider abstractions for payment, email, SMS, tracking, and storage
- Cloudinary for assets
- Razorpay for payments/refunds
- Brevo for email
- Configurable SMS provider
- Configurable shipment-tracking provider
- Enum values persisted as strings
- UTC timestamps in database; configurable store timezone for display
- Decimal for money
- Strong validation, idempotency, rate limiting, webhook verification, structured logging, health checks, and tests

## Deployment model
Each business is an isolated deployment:
- Frontend
- .NET API
- PostgreSQL database
- Cloudinary shared infrastructure initially, separated by customer folder/prefix
- Customer-specific domain and credentials

No shared multi-tenant database is required in V1.

## Definition of done
A customer can be onboarded by deploying the application, applying basic branding/infrastructure configuration, and giving the business owner Admin access. The owner can then configure catalog, pricing, stock, promotions, delivery, payments, authentication, content, policies, and integrations without code changes.

## Scope
See:
- 01-Architecture.md
- 02-Domain-Model.md
- 03-Configuration.md
- 04-Authentication.md
- 05-Catalog-Inventory.md
- 06-Cart-Checkout-Orders.md
- 07-Payments-Razorpay.md
- 08-Promotions-Coupons.md
- 09-Shipping-Tracking.md
- 10-Email-SMS.md
- 11-Admin.md
- 12-Real-Time-Outbox-Webhooks.md
- 13-Caching-Performance.md
- 14-Security.md
- 15-Testing.md
- 16-API-Standards.md
- 17-Deployment-Operations.md
- 18-Implementation-Plan.md
