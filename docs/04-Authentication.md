# Authentication and Authorization

## Customer authentication
Support:
- Google OAuth
- Email/password
- Mobile OTP

Guest checkout may be supported, but mobile verification is required before placing an order if the store enables that rule.

Google identity is used only to establish identity. The application issues its own access/refresh tokens.

## JWT
- Short-lived access token
- Refresh token rotation
- Refresh-token revocation
- Token versioning
- Token hash stored, not raw refresh token
- Session/device tracking where useful

## Token versioning
User has TokenVersion. Incrementing it invalidates existing access tokens.

## Refresh token
Store:
- Hash
- UserId
- CreatedAt
- ExpiresAt
- RevokedAt
- ReplacedBy
- Device metadata where appropriate

## Roles
At minimum:
- Customer
- Admin

Add SuperAdmin only if a real requirement emerges.

## Authorization
Use policies/roles rather than controller-level ad hoc checks.

## OTP
Generate cryptographically secure OTP.
Store only a hash.
Track expiry and attempts.
Rate-limit requests.
Prevent brute force.
Never log the OTP.

## Passwords
Use ASP.NET Core PasswordHasher or a vetted password hashing mechanism. Never store plaintext passwords.

## Security
- Rate-limit login/OTP/refresh/password reset
- Lock or throttle abusive flows
- Validate OAuth state/nonce correctly
- Use secure secrets
- Never log tokens or credentials
