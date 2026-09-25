# Google OAuth Integration

## Purpose

Google provides identity authentication. The application issues its own JWT/refresh-token session.

## Flow

```text
Frontend
  -> Google authorization
  -> OAuth callback
  -> Validate authorization result
  -> Find/create local user
  -> Issue application tokens
```

## Configuration

- Client ID
- Client secret
- Redirect URI
- Allowed frontend origins as applicable

Secrets are deployment configuration.

## Security

Validate:
- OAuth state
- nonce where applicable
- issuer
- audience/client ID
- token signature/claims
- redirect URI

Never accept an arbitrary Google token without validation.

## User mapping

Persist stable provider identity:
- Provider = Google
- ProviderSubject/ExternalId
- UserId

Do not use email alone as the permanent external identity key.

## Account linking

If the business enables account linking, define explicit rules to prevent account takeover through unverified email assumptions.

## Output

After successful Google authentication:
- Find/create local customer
- Update safe profile information
- Issue application access token
- Issue rotated refresh token

Google access/ID tokens are not used as application API credentials.
