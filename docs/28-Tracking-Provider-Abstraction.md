# Tracking Provider Abstraction

## Purpose

Small businesses may use different courier partners. The application therefore normalizes tracking into an internal model.

## Interface

```text
ITrackingProvider
  TrackAsync(trackingNumber, carrier)
  DetectCarrierAsync(trackingNumber)
```

Provider-specific APIs remain inside Infrastructure.

## Internal result

```text
TrackingResult
- TrackingNumber
- Carrier
- CurrentStatus
- EstimatedDelivery
- Events[]
```

## Normalized status

- Pending
- Shipped
- InTransit
- OutForDelivery
- Delivered
- Exception
- Returned
- Unknown

## Flow

```text
Admin enters tracking number
  -> Shipment created
  -> ITrackingProvider
  -> Provider
  -> Normalize result
  -> Persist events
  -> SignalR
```

## Manual fallback

Admin can manually update shipment status when:
- Provider has no result
- Courier is unsupported
- Provider API is unavailable

Manual status must be marked as manual.

## Customer experience

Customer sees an internal tracking timeline. A provider tracking URL can be offered as an optional external link.

## No frontend polling

Frontend receives updates via SignalR. Initial/current state comes from API.

## Provider requirements

Concrete tracking providers must document:
- Tracking endpoint
- Carrier detection
- Authentication
- Rate limits
- Pricing
- Free tier
- Webhook support
- Event format
- Estimated delivery availability
- Error mapping
- Retry rules
