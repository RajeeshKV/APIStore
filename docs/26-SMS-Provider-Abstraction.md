# SMS Provider Abstraction

> Implemented. See [28-SMS-Providers.md](28-SMS-Providers.md) for the current contract,
> supported providers, configuration module, and verification enforcement. This document is
> the original design intent and is kept for history.

## Purpose

OTP SMS must be provider-independent.

## Interface

```text
ISmsProvider
  ProviderName
  Kind
  IsOperational
  SendOtpAsync(...)
```

Only OTP delivery is abstracted today. A generic `SendAsync(...)` was specified here but was
never implemented — order, shipping, and refund SMS have no provider path yet, so adding it is
a real gap rather than a rename.

Provider implementations must not contain business OTP generation/verification logic.

## Application flow

```text
Request OTP
  -> Check a provider is operational
  -> Generate secure OTP
  -> Deliver via ISmsProvider.SendOtpAsync
  -> On success: hash OTP and store the request
```

Delivery happens before persistence so a failed send cannot leave an unverifiable code in the
table consuming a resend cooldown.

Verification:
```text
Verify OTP
  -> Canonicalise the submitted number
  -> Load request
  -> Check expiry
  -> Check attempts
  -> Hash submitted OTP
  -> Compare (constant time)
  -> Mark verified
  -> Promote the account phone to verified
```

## Configuration

- Selected provider (exactly one)
- Per-provider credentials in strongly-typed options
- Sender ID / template configuration as required
- Country
- OTP policy: expiry, cooldown, attempts, length

## Security

- Never store plaintext OTP
- Never log OTP or credentials
- Rate-limit send
- Rate-limit verify
- Cooldown resends
- Maximum attempts
- Invalidate previous OTP when appropriate

## Provider contract

Each provider document must define:
- Endpoint
- Auth
- Request mapping
- Response mapping
- Error mapping
- DLT/template requirements
- Rate limits
- Pricing/free quota at implementation time

## Provider selection

One configured SMS provider is active, selected in the `Sms` configuration section. Resolution
goes through `ISmsProviderFactory` so the single-active-provider rule cannot be bypassed.

Provider credentials must never be returned in read APIs. The status endpoint reports
configuration *key names* that are missing, never their values.

## Failure

If provider fails, mark OTP send as failed and allow controlled retry. Do not generate unlimited
new OTPs.
