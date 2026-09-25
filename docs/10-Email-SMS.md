# Email and SMS

## Email
Provider abstraction:
```text
IEmailProvider
  -> KromicManagedBrevoProvider
  -> CustomerBrevoProvider
```

### Kromic Managed
Small businesses can use Kromic's Brevo account.

### Customer Brevo
Customer supplies provider credentials/configuration and verified sender information.

### Templates
Email templates are code-owned and generic. Do not build an email-template CMS in V1.

Templates include:
- Welcome
- Password reset
- Order confirmation
- Payment successful
- Payment failed
- Order shipped
- Order delivered
- Order rejected
- Refund initiated
- Refund completed

Dynamic values:
- Business logo
- Business name
- Website
- Support email
- Phone
- Customer name
- Order number
- Amount
- Tracking number
- Expected delivery

## SMS
Provider abstraction:
```text
ISmsProvider
  -> TechTo
  -> SpringEdge
  -> 2Factor
```

Admin can select provider and configure credentials.

SMS is primarily for OTP in V1.

## OTP controls
- 6-digit default
- Configurable expiry
- Resend cooldown
- Attempt limit
- Rate limiting
- Hashed storage

## DLT
Production India SMS must account for DLT/entity/sender/template requirements as applicable.

## Email delivery
Use Outbox/background processing. Never block an order request waiting for email delivery.

## Provider failure
Retry transient failures. Do not blindly send duplicate messages after uncertain provider responses.
