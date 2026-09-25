# Architecture

## Solution structure
```text
src/
  KromicCommerce.Api
  KromicCommerce.Application
  KromicCommerce.Contracts
  KromicCommerce.Domain
  KromicCommerce.Infrastructure
  KromicCommerce.Shared

tests/
  KromicCommerce.UnitTests
  KromicCommerce.IntegrationTests
  KromicCommerce.ApiTests
```

## Dependency direction
API -> Application -> Domain
Infrastructure -> Application + Domain
Domain must not depend on Infrastructure or external providers.

## Application
Contains commands, queries, handlers, validators, behaviors, interfaces, business orchestration, and mapping.

## Domain
Contains entities, value objects, enums, domain rules, and domain events. No EF Core, HTTP, provider SDK, or infrastructure dependencies.

## Contracts
Contains API request/response DTOs and shared contracts. Never expose EF entities directly.

## Infrastructure
Contains EF Core/Npgsql, repositories, external-provider adapters, authentication implementations, caching, background workers, email/SMS/payment/tracking/storage integrations.

## Patterns
Use patterns only where they solve a real problem:
- Repository for aggregate/data boundaries
- Strategy for selectable providers
- Adapter for third-party SDK/API normalization
- Proxy/decorator for cross-cutting provider behavior where useful
- Factory when object/provider creation genuinely requires it
- Specification only for reusable complex query rules
- Outbox for reliable asynchronous side effects
- Mediator for CQRS
Do not create abstractions that merely wrap one trivial method.

## API
Controllers remain thin. They validate transport concerns and dispatch commands/queries.

## Transactions
EF Core DbContext is the primary unit-of-work mechanism. Do not add an artificial UnitOfWork wrapper unless an explicit abstraction is needed.

## Background processing
Use a hosted worker for outbox processing, retries, asynchronous emails, SignalR notifications, and other non-request-critical tasks.

## Real-time
SignalR broadcasts changes. Database/API remains the source of truth. Clients must recover state from API after reconnecting.
