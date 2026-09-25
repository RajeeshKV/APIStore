# SMS Provider Implementations

This file is the implementation contract for concrete SMS providers. Provider availability, pricing, API endpoints, SDK behavior, free credits, and DLT requirements must be verified against the provider's current official documentation before implementation.

## Provider slots

V1 should support up to three interchangeable providers.

Recommended abstraction:
```text
ISmsProvider
  -> ProviderA
  -> ProviderB
  -> ProviderC
```

## Required implementation details per provider

For every selected provider document:
1. Official API base URL
2. Authentication
3. OTP/send endpoint
4. Request schema
5. Response schema
6. Success criteria
7. Error codes
8. Rate limits
9. Free credits/quota if currently offered
10. Per-message pricing
11. Sender/template requirements
12. DLT requirements for India
13. Timeout
14. Retryable errors
15. Non-retryable errors
16. Sandbox/testing
17. Webhook support if available
18. Credential configuration
19. DI registration
20. Integration tests

## Important

Do not hard-code pricing or free-quota assumptions in application logic. Provider commercial terms can change.

## Provider adapter contract

A provider adapter should return an internal result such as:
```text
SmsSendResult
- Success
- ProviderMessageId
- ErrorCode
- ErrorMessage
- Retryable
```

The rest of the application must not depend on provider-specific response models.
