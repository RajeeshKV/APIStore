---
name: payment-integration
description: Use when implementing Razorpay payments, payment verification, refunds, payment webhooks, or financial state transitions.
---

# Payment Integration

Read:
- `docs/24-Razorpay-Integration.md`
- payment/order documentation
- webhook documentation

Rules:
- Payment provider is accessed through IPaymentProvider.
- Never trust frontend payment success alone.
- Verify provider-side state/signatures.
- Payment webhooks are idempotent.
- Never blindly retry ambiguous financial mutations.
- Refunds must be bounded by captured amount.
- Financial state transitions must be explicit.
- Use Outbox for post-transaction notifications.
- Add tests for duplicate webhooks, timeout/ambiguous state, failed payment, and refund.
