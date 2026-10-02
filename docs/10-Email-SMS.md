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
ISmsProvider  (resolved via ISmsProviderFactory — one active at a time)
  -> 2Factor
  -> Free2SMS
  -> Twilio (Programmable Messaging)
```

Admin can select a provider and configure credentials. No template management: each adapter
hardcodes its HTTP endpoint and variable mapping, and only `{{OTP}}` is substituted. See
`docs/28-SMS-Providers.md`.

The application always generates and verifies the OTP itself; no adapter delegates code
generation to the gateway, so expiry, attempt limits and cooldown behave identically whichever
provider is active.

SMS is primarily for OTP in V1.

## OTP controls
- 4-digit default (fixed)
- Fixed expiry: 5 minutes
- Fixed resend cooldown: 60 seconds
- Fixed attempt limit: 5
- Rate limiting (5 requests / 60 seconds per IP)
- Hashed storage (SHA-256)

## DLT
Production India SMS must account for DLT/entity/sender/template requirements as applicable.

## Email delivery
Use Outbox/background processing. Never block an order request waiting for email delivery.

## Provider failure
Retry transient failures. Do not blindly send duplicate messages after uncertain provider responses.
