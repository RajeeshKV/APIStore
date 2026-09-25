---
name: testing
description: Use when adding tests, debugging failing tests, validating a completed phase, or reviewing production readiness.
---

# Testing Workflow

1. Identify the changed behavior.
2. Run focused tests first.
3. Fix failures rather than weakening assertions.
4. Add regression tests for bugs.
5. Mock external providers at application/domain tests.
6. Test provider adapters separately.
7. Test transaction boundaries and idempotency for critical flows.
8. Run the full relevant suite before completing the task.
9. Report remaining failures explicitly.
