# Brevo Integration

## Purpose

Brevo is the default email provider.

## Provider model

```text
IEmailProvider
  |
  +-- ManagedBrevoEmailProvider
  +-- CustomerBrevoEmailProvider
```

## Modes

### Managed mode
Kromic's Brevo account sends emails for smaller deployments.

### Customer mode
Customer supplies their own Brevo configuration and verified sender.

## Configuration

Provider configuration should support:
- API key/secret as applicable
- Sender email
- Sender name
- Optional reply-to
- Provider mode

Secrets are never returned from Admin APIs.

## Email templates

Templates remain code-owned in V1.

Generic templates:
- Welcome
- Order confirmation
- Payment success
- Payment failure
- Order shipped
- Order delivered
- Order rejected
- Refund initiated
- Refund completed
- Password reset

Business branding is injected dynamically:
- Logo
- Business name
- Website
- Support contact

## Flow

```text
Business event
  -> OutboxEvent
  -> Background worker
  -> IEmailProvider
  -> Brevo
```

HTTP request should not wait for email delivery.

## Failure handling

Transient provider failures are retried through outbox processing.

Permanent failures are recorded and surfaced for operational review.

## Rate limits

Respect the account's current Brevo limits. Do not assume a universal free monthly quota.

## Deliverability

Customer must configure/verify appropriate sender/domain authentication where required.

## Security

Never log API keys or full message contents if they contain sensitive customer information.

## Testing

Use a fake IEmailProvider for unit tests and sandbox/test sender configuration for integration tests.
