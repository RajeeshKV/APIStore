# Delivery and Tracking

## Delivery calculation
V1 supports:
- Flat delivery fee
- Free shipping threshold
- Processing days
- Delivery days

Future:
- Pincode
- Zone
- Weight
- Order-value rules

Expected delivery is calculated from configured processing + delivery rules and store timezone/calendar rules.

## Shipment
Admin enters:
- Courier (optional if provider auto-detects)
- Tracking number
- Shipment mode

## Provider abstraction
```text
ITrackingProvider
  -> TrackCourierProvider
  -> Future provider
  -> ManualTrackingProvider
```

## Tracking
Provider may:
- Auto-detect carrier
- Return current status
- Return event timeline
- Return location
- Send webhooks

Persist normalized events in ShipmentTrackingEvent.

## Customer UI
Show your own tracking timeline. Provider website is only an optional fallback link.

## Manual fallback
Admin can update normalized statuses manually when automatic tracking is unavailable.

## Webhooks
Verify signatures where supported. Persist event/provider IDs and enforce idempotency.

## Polling
Frontend must not poll. Use SignalR for updates. Backend can use provider webhooks and controlled background refreshes where necessary.
