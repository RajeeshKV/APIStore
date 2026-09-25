# Outbox Integration

## Purpose

Guarantee reliable asynchronous side effects after a successful database transaction.

## Example

```text
Order transaction
  -> Order updated
  -> Outbox event inserted
  -> COMMIT
  -> Worker
  -> Email / SignalR / other handler
```

## Events

Examples:
- OrderCreated
- OrderConfirmed
- OrderRejected
- PaymentSucceeded
- PaymentFailed
- RefundInitiated
- RefundCompleted
- ShipmentCreated
- ShipmentUpdated

## Processing

Worker:
1. Claims available event
2. Deserializes payload
3. Executes handler
4. Marks processed
5. Retries transient failures
6. Records final failure after max attempts

## Idempotency

Every handler must tolerate duplicate execution.

Example:
Sending an email should have a stable logical event ID so a retry does not accidentally send unlimited duplicates where provider-level idempotency is available.

## Transaction boundary

Outbox event insertion occurs in the same database transaction as the business state mutation.

## Dead-letter

After bounded retries, retain event and error information for operational review/reprocessing.
