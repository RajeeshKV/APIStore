---
name: database-development
description: Use when changing EF Core entities, DbContext, migrations, indexes, relationships, PostgreSQL configuration, transactions, or persistence behavior.
---

# Database Development

1. Read the relevant domain/data documentation.
2. PostgreSQL is the transactional source of truth.
3. Use EF Core/Npgsql.
4. Persist enums as strings.
5. Use decimal for money.
6. Use UTC timestamps.
7. Configure relationships/indexes/constraints explicitly.
8. Protect important business invariants with database constraints where appropriate.
9. Generate migrations for schema changes.
10. Never silently destroy or reset existing data.
11. Add tests for important persistence behavior.
12. Consider concurrency/idempotency for stock, payment, order, and webhook state.
