---
inclusion: always
---

# Kromic Commerce — Project Rules

Reusable isolated-deployment ecommerce platform for small businesses.

## Stack
- .NET 8 / ASP.NET Core
- PostgreSQL / EF Core / Npgsql
- Clean Architecture
- CQRS + Mediator
- Separate DTO/Contracts project
- JWT + refresh-token rotation + token versioning
- Google OAuth
- SignalR
- IMemoryCache
- Serilog
- FluentValidation

## Core rules
- Domain must not depend on Infrastructure.
- API must not expose EF entities.
- Persist enums as strings.
- Use decimal for money.
- Store timestamps in UTC.
- Infrastructure secrets come from environment variables.
- Business configuration belongs in PostgreSQL and is controlled through Admin.
- External providers are accessed through abstractions.
- SignalR is notification only; API/database remains authoritative.
- Never trust client-side prices, discounts, tax, stock, or totals.
- Do not introduce Redis in V1.
- Keep Program.cs minimal.
- Do not add patterns merely for ceremony.

Detailed requirements live under /docs. Load only documentation relevant to the current task.
