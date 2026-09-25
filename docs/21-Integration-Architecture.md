# Integration Architecture

This document defines the common integration architecture for all external providers.

## Rule

The Application layer depends on business-facing interfaces. Infrastructure contains provider implementations.

```text
Application
  |
  +-- IPaymentProvider
  +-- IEmailProvider
  +-- ISmsProvider
  +-- ITrackingProvider
  +-- IStorageProvider
  |
  v
Infrastructure
  |
  +-- RazorpayPaymentProvider
  +-- BrevoEmailProvider
  +-- SmsProvider implementations
  +-- TrackingProvider implementations
  +-- CloudinaryStorageProvider
```

## Provider configuration

Provider configuration is represented by strongly typed Options classes.

Never scatter raw configuration lookups through application code.

## Provider requirements

Every provider implementation must define:
- Configuration model
- Credential requirements
- Client/SDK registration
- Timeout policy
- Retry policy
- Error mapping
- Logging rules
- Health/diagnostic behavior where useful
- Test strategy

## Error model

External provider exceptions must not leak into Domain/Application.

Map provider failures to application-level errors such as:
- ProviderUnavailable
- ProviderTimeout
- ProviderRejected
- InvalidProviderConfiguration
- ProviderRateLimited
- ProviderAuthenticationFailed

## Retry

Retry only transient failures. Never blindly retry:
- Invalid credentials
- Invalid requests
- Business rejection
- Already-processed operations

Use exponential backoff and bounded attempts.

## Timeouts

Every outbound HTTP integration must have an explicit timeout.

## HTTP client

Use IHttpClientFactory/typed clients where HTTP is involved.

## Secrets

Secrets come from environment/secret storage. Never commit credentials.

## Observability

Provider calls should include:
- Trace/correlation ID
- Provider name
- Operation
- Duration
- Success/failure
- Provider request/event ID when safe

Never log credentials, tokens, OTPs, or payment secrets.

## Testing

Provider implementations must support:
- Unit tests with mocked transport
- Contract/request mapping tests
- Sandbox integration tests where available
- Webhook tests where applicable

## Provider selection

Selectable providers use Strategy-style resolution:
```text
ConfiguredProvider = "Razorpay"
        |
        v
IPaymentProviderResolver
        |
        v
RazorpayPaymentProvider
```

Only one configured provider is used for each integration category unless the business explicitly supports fallback.

## Fallback

Fallback providers must not silently execute a second financial transaction after an ambiguous first-provider response. Fallback is appropriate mainly for non-financial services such as SMS/tracking, and only with clear idempotency rules.
