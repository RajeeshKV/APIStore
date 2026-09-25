---
name: shipping-tracking
description: Use when implementing shipment creation, tracking numbers, courier tracking, tracking providers, delivery status, manual shipment updates, or tracking webhooks.
---

# Shipping and Tracking

Read:
- tracking abstraction documentation
- concrete tracking integration documentation
- order/shipping documentation

Rules:
- Admin enters tracking number/carrier after shipment.
- Normalize provider statuses into internal shipment statuses.
- Support manual admin status updates.
- Store last known tracking state.
- Prefer provider webhooks when available.
- Frontend must not poll tracking APIs.
- SignalR communicates status changes.
- A provider tracking URL may be shown as an optional external link.
- Provider failures must not erase the last known state.
