# Engineering Rules

1. Keep Program.cs small.
2. Put service registration in extension/helper methods.
3. Do not expose EF entities from API.
4. Do not put business logic in controllers.
5. Do not put business rules in Infrastructure.
6. Domain must remain infrastructure-agnostic.
7. Do not use double/float for money.
8. Store enum values as strings.
9. Store timestamps in UTC.
10. Do not trust frontend totals.
11. Do not use cache as a transactional source of truth.
12. Do not poll from frontend for real-time state.
13. SignalR is notification; API/database is truth.
14. Verify every external webhook.
15. Make financial/webhook/outbox operations idempotent.
16. Rate-limit OTP/authentication endpoints.
17. Never log secrets/tokens/OTP.
18. Don't create an interface unless it provides a real abstraction.
19. Don't implement a design pattern merely for architectural appearance.
20. Prefer simple solutions until complexity is justified.
21. External providers must be behind application-facing interfaces.
22. Provider SDK types must not leak into Domain.
23. Use cancellation tokens for async I/O.
24. Use async APIs end-to-end for I/O.
25. Use database constraints in addition to application validation.
26. Test concurrency-sensitive business logic.
27. Cache read-heavy configuration/catalog data.
28. Invalidate cache after successful mutations.
29. Use Cloudinary for image storage; don't store image binaries in PostgreSQL.
30. Every production feature must have logging, validation, authorization, and tests.
