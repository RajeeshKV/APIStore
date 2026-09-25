# Webhook Integration

## Purpose

Provide one secure pattern for Razorpay, tracking, and future provider webhooks.

## Processing pipeline

```text
HTTP request
  -> Verify signature
  -> Parse event
  -> Validate provider event ID
  -> Idempotency check
  -> Persist receipt
  -> Apply domain/application state change
  -> Create Outbox event
  -> Commit
  -> Return success
```

## Security

Each provider must define its signature verification method. Never accept unsigned provider claims when the provider supports signatures.

## Idempotency

Persist:
- Provider
- Event ID
- Event type
- ReceivedAt
- ProcessedAt
- Status
- Failure reason

Duplicate event ID must not execute business side effects twice.

## Response

Return provider-appropriate success quickly. Do not perform slow email/SMS operations inside webhook HTTP handling.

## Failure

For processing failures:
- Record failure
- Return an appropriate response according to provider retry semantics
- Let provider retry when safe
- Or use internal retry/outbox strategy where appropriate

## Logging

Log provider + event ID + trace ID, never secrets.
