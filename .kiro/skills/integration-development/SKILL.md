---
name: integration-development
description: Use when implementing or modifying any external provider integration such as Razorpay, Brevo, Cloudinary, SMS, tracking, Google OAuth, or webhooks.
---

# Integration Development

## Before coding

Read:
- `docs/21-Integration-Architecture.md`
- The provider-specific document relevant to the task
- Relevant API/security/testing documentation

Do not load unrelated provider documents.

## Rules

1. Application depends on a business-facing interface.
2. Provider SDKs/types stay in Infrastructure.
3. Use typed Options for provider configuration.
4. Credentials come from environment variables.
5. Add timeout handling.
6. Retry only transient failures.
7. Map provider errors to application errors.
8. Implement idempotency for repeatable operations.
9. Verify webhook signatures where supported.
10. Never expose provider DTOs through API contracts.
11. Never log credentials, tokens, OTPs, or secrets.
12. Add unit tests for the adapter.
13. Add sandbox/integration tests where appropriate.
14. Add/update `.env.example` for new configuration keys.
15. Update relevant documentation when the provider contract changes.

## Implementation flow

```text
Application interface
    -> Infrastructure adapter
    -> Typed HTTP client/SDK
    -> Provider
    -> Provider response
    -> Internal result
```

Do not allow provider-specific behavior to leak into Domain.
