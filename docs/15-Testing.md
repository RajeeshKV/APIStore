# Testing Strategy

## Unit tests
Focus on business rules:
- Price calculations
- Tax
- Delivery
- Coupons
- Promotions
- Buy X Get Y
- Order state transitions
- Refund rules
- Inventory reservations
- OTP rules

## Integration tests
Test:
- PostgreSQL/EF mappings
- Transactions
- Enum string persistence
- Concurrency
- Outbox
- Webhook idempotency
- Authentication flows

## API tests
Test:
- Authorization
- Validation
- Error responses
- Customer endpoints
- Admin endpoints
- Pagination/filtering/sorting

## Provider tests
Use mocks/fakes for unit tests. Use sandbox/test credentials for provider integration tests.

## Critical scenarios
- Two customers purchase last item concurrently
- Duplicate Razorpay webhook
- Duplicate shipment webhook
- Refund retry
- Provider timeout
- Email provider failure
- SMS provider failure
- Cache invalidation after update
- Refresh token rotation/reuse
- Admin unauthorized access

## Test quality
Avoid testing implementation details. Test externally observable behavior and business invariants.
