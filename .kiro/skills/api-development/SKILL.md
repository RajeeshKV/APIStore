---
name: api-development
description: Use when creating or modifying ASP.NET Core endpoints, controllers, DTOs, commands, queries, validation, authorization, or API contracts.
---

# API Development

1. Read the relevant feature documentation.
2. Keep controllers thin.
3. Create request/response DTOs in the Contracts/DTO project.
4. Use CQRS handlers for application behavior.
5. Validate input.
6. Enforce authorization at the correct boundary.
7. Never expose EF entities.
8. Return consistent API results/errors.
9. Pass CancellationToken.
10. Add tests for success, validation, authorization, not-found, and business-rule failures.
11. Update Swagger/OpenAPI metadata when useful.
