# Tracking Provider Implementation

The concrete tracking provider must be selected after validating current API availability, pricing, coverage for Indian couriers, free/cheap limits, carrier detection, and webhook support.

## Adapter requirements

Implement:
```text
ITrackingProvider
```

with:
- HTTP client
- Typed configuration
- Provider response DTOs
- Mapping to TrackingResult
- Status mapping
- Error mapping
- Retry/timeout policy
- Logging
- Tests

## Provider response must never leak

Do not expose raw provider JSON to the storefront.

Normalize:
```text
Provider status -> Internal ShipmentStatus
Provider event -> ShipmentTrackingEvent
Provider ETA -> ExpectedDelivery
```

## Polling

Backend background refresh is allowed only when:
- Provider has no webhook
- Tracking data is expected to change
- Rate limits are respected

Frontend polling is prohibited.

## Webhook-capable provider

Prefer webhook-driven updates:
```text
Provider
 -> Webhook
 -> Verify
 -> Idempotency
 -> Persist
 -> Outbox
 -> SignalR
```

## Fallback

If provider tracking fails, customer still sees the last known internal status and tracking number.

Admin can manually update status.
