# Integration Testing

## Goal

Ensure external integrations can change without breaking core commerce logic.

## Test layers

### Unit
Mock provider interfaces.

### Contract/mapping
Test provider response -> internal model mapping.

### Sandbox
Run against official test/sandbox environments where available.

### Webhook
Test:
- Valid signature
- Invalid signature
- Duplicate event
- Unknown event
- Out-of-order event
- Retry behavior

## Payment tests
- Payment success
- Payment failure
- Duplicate success webhook
- Refund
- Duplicate refund notification
- Provider timeout
- Ambiguous payment status

## Email tests
- Correct template data
- Provider failure
- Retry
- Invalid sender/configuration

## SMS tests
- OTP sent
- Provider rejected
- Rate limited
- Timeout
- Invalid credentials

## Tracking tests
- Delivered
- In transit
- Out for delivery
- Exception
- Unknown tracking number
- Provider timeout
- Duplicate event

## Cloudinary tests
- Upload success
- Invalid file
- Provider failure
- Delete/replace
- Metadata persistence

## Principle

Core business tests should not require live external providers.
