# Security

## API
- HTTPS
- HSTS in production
- CORS allowlist
- Authentication/authorization
- Rate limiting
- Request size limits
- Input validation
- Secure headers
- Consistent error handling

## Authentication
- Hash passwords
- Hash refresh tokens
- Secure OAuth state/nonce
- Token rotation
- Token versioning
- Revoke compromised sessions

## Secrets
Never store secrets in source control or database plaintext unless encrypted and required.
Use environment/secret storage.

## Logging
Never log:
- Passwords
- OTP
- Access tokens
- Refresh tokens
- API secrets
- Payment secrets

## File uploads
- Validate MIME/type
- Validate extension
- Limit size
- Restrict uploads to expected media
- Prefer Cloudinary transformations
- Do not serve arbitrary executable content

## Webhooks
Verify signatures and enforce idempotency.

## Public APIs
Rate-limit:
- Login
- OTP send
- OTP verify
- Refresh
- Password reset
- Coupon validation
- Search where abuse is possible

## Authorization
Every admin mutation must be authorized server-side.

## Business logic
Never trust:
- Client price
- Client discount
- Client stock
- Client tax
- Client shipping fee
- Client role
