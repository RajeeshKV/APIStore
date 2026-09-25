# SignalR Integration

## Purpose

Provide real-time state-change notifications without frontend polling.

## Hub

Example:
```text
/storeHub
```

## Event types
- order.updated
- payment.updated
- shipment.updated
- inventory.updated
- admin.order.updated

## Flow

```text
Database transaction
  -> OutboxEvent
  -> Background worker
  -> SignalR publisher
  -> Connected clients
```

Do not publish a real-time event before the database transaction is committed.

## Groups

Use secure groups such as:
- customer:{customerId}
- order:{orderId}
- admin

Authorization is mandatory.

## Reconnection

Frontend reconnects automatically and reloads authoritative state from API.

## Event payload

Send small, non-sensitive payloads:
```text
EntityId
EventType
CurrentStatus
UpdatedAt
```

Do not send secrets or unnecessary personal data.

## Scaling

V1 assumes one API instance and in-process SignalR.

If the deployment later requires multiple API instances, introduce a supported SignalR backplane/service.
