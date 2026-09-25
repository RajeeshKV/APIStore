# SMS Provider Abstraction

## Purpose

OTP SMS must be provider-independent.

## Interface

```text
ISmsProvider
  SendOtpAsync(...)
  SendAsync(...)
```

Provider implementation must not contain business OTP generation/verification logic.

## Application flow

```text
Request OTP
  -> Generate secure OTP
  -> Hash OTP
  -> Store request
  -> ISmsProvider.SendOtpAsync
```

Verification:
```text
Verify OTP
  -> Load request
  -> Check expiry
  -> Check attempts
  -> Hash submitted OTP
  -> Compare
  -> Mark verified
```

## Configuration

- Selected provider
- API credentials
- Sender ID/template configuration as required
- Country
- Provider-specific options

## Security

- Never store plaintext OTP
- Never log OTP
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

Admin selects one configured SMS provider.

Provider credentials must never be returned in read APIs.

## Failure

If provider fails, mark OTP send as failed and allow controlled retry. Do not generate unlimited new OTPs.
