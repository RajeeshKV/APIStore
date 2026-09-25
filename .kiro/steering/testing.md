---
inclusion: fileMatch
fileMatchPattern:
  - "tests/**/*.cs"
  - "**/*Tests.cs"
  - "**/*Test.cs"
---

# Testing Rules

- Core business logic must be testable without live external providers.
- Mock provider interfaces in unit tests.
- Test validation, authorization, state transitions, idempotency, and error handling.
- Provider adapters require mapping/contract tests.
- Webhook integrations require valid signature, invalid signature, duplicate, retry, and unknown-event tests.
- Financial operations require duplicate/ambiguous-state tests.
- Run the full relevant test suite before declaring a phase complete.
