# Real-Time, Outbox and Webhooks

## SignalR
Use SignalR for:
- Order status updates
- Shipment updates
- Payment status
- Admin dashboard updates
- Inventory notifications where useful

SignalR is not the source of truth.

## Outbox
Create OutboxEvent in the same database transaction as the business change.

Example:
```text
Order transaction
  -> Order updated
  -> OutboxEvent created
  -> Commit
  -> Worker publishes event
```

## Outbox fields
- Id
- Type
- Payload
- CreatedAt
- ProcessedAt
- Attempts
- LastError
- Status

## Worker
- Poll/claim unprocessed events
- Process safely
- Retry transient errors
- Mark successful
- Dead-letter or alert after configurable attempts

## Webhooks
Every provider webhook:
1. Verify signature/authentication
2. Parse and validate
3. Check idempotency/provider event ID
4. Persist receipt
5. Apply state transition
6. Create outbox event if needed
7. Return success quickly

## Webhook security
Never trust provider payloads without verification when the provider offers a signature mechanism.
