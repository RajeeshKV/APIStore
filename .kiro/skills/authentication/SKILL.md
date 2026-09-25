---
name: authentication
description: Use when implementing login, JWT, refresh tokens, token rotation, token versioning, Google OAuth, customer identity, OTP, or authorization.
---

# Authentication

Read:
- Authentication documentation
- Google OAuth documentation
- SMS documentation when OTP is involved

Rules:
- Google authenticates identity; application issues its own JWT/refresh tokens.
- Refresh tokens are rotated and revocable.
- Token versioning supports invalidation.
- Never use provider tokens as application API credentials.
- Validate OAuth state/issuer/audience/redirect URI as applicable.
- OTPs are hashed, short-lived, rate-limited, and never logged.
- Authorization must be server-side.
- Never expose secrets in API responses.
