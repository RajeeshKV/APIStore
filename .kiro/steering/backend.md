---
inclusion: fileMatch
fileMatchPattern:
  - "src/**/*.cs"
  - "tests/**/*.cs"
---

# Backend Engineering Rules

- Keep controllers thin.
- Use CQRS for application use cases.
- Use DTOs/contracts at API boundaries.
- Validate commands/queries with FluentValidation where applicable.
- Pass CancellationToken through async operations.
- Use strongly typed Options for configuration.
- Never scatter raw IConfiguration lookups through business code.
- External SDKs and provider DTOs stay in Infrastructure.
- Map external errors into application-level errors.
- Use async I/O.
- Avoid unnecessary repository abstractions; follow the architecture docs.
- Use transactions for state changes that require atomicity.
- Use idempotency for webhooks, financial mutations, and retried side effects.
- Never log secrets, tokens, OTPs, payment credentials, or sensitive payloads.
