# SMS / OTP — Admin Setup & Customer Flow

Two documents in one: how an administrator configures a gateway, and what a customer then
experiences. Companion to `36-SMS-OTP-UI-Integration-Guide.md`, which holds the low-level API
contract for the customer-facing OTP endpoints.

---

## Part 1 — Admin configuration

### Why there are two screens

Provider selection and OTP policy are **separate concerns owned by separate endpoints**, and the
frontend should keep them separate:

| Question | Endpoint | Field |
|----------|----------|-------|
| *Which gateway do we use, and with what credentials?* | `PUT /api/v1/admin/integrations/sms` | `provider`, `providerSettings` |
| *How long does a code live, how often may it be resent?* | `PUT /api/v1/admin/settings/auth` | `otpExpiryMinutes`, `otpResendCooldownSeconds`, `otpMaxAttempts` |

They are not one form because the two answers live in different places: the gateway is persisted in
the `SmsProviderConfig` singleton and read by the provider factory, while the OTP timings live in
business settings. Merging them would create a second, competing copy of "which provider is active"
that could silently disagree with the one the backend actually sends through.

> `settings/auth` accepts an optional `smsProvider` field for backward compatibility only. **Ignore
> it.** It has no effect on delivery, and omitting it is the correct thing to do.

### Two-step flow: `integrations/sms`

**Read the current state**

```http
GET /api/v1/admin/integrations/sms
```

```json
{
  "integrationName": "SMS",
  "enabled": false,
  "isConfigured": false,
  "maskedKeyId": null,
  "hasSecret": false,
  "publicFields": {
    "provider": "2Factor",
    "requireVerifiedPhoneAtCheckout": "False",
    "selectableProviders": "2Factor, Free2SMS, Twilio",
    "selectedProvider": "2Factor",
    "selectedProviderEnabled": "False",
    "configuredSettings": "apiKey"
  }
}
```

Field meanings that drive the UI:

| Field | Meaning |
|-------|---------|
| `enabled` | Master switch. `false` means no SMS is sent at all. |
| `isConfigured` | `enabled` **and** every required setting present. **This is the "can it send?" flag.** |
| `hasSecret` | A credential has been stored. |
| `publicFields.provider` | The gateway currently in effect. |
| `publicFields.selectableProviders` | Comma-separated list to render the provider dropdown from. |
| `publicFields.configuredSettings` | Setting **names** already stored. Never values — secrets are never returned. |
| `publicFields.missingSettings` | Configuration paths still needed. Present only when something is missing. |
| `publicFields.requireVerifiedPhoneAtCheckout` | Whether checkout will demand a verified phone. |

**Save — the response tells you what happened**

```http
PUT /api/v1/admin/integrations/sms
{ "enabled": true, "provider": "2Factor", "providerSettings": { "apiKey": "…" } }
```

Returns **`200` with the same body as the GET**, not `204 No Content`. Use the returned object
directly to re-render the screen; do not follow up with a GET.

This is what makes the staged setup below possible.

#### Staged setup (recommended)

Because enabling requires a complete configuration, credentials are entered in two steps:

1. **Save with `enabled: false`.** Partial settings are accepted and stored.

   ```json
   { "enabled": false, "provider": "Twilio",
     "providerSettings": { "accountSid": "AC…" } }
   ```
   → `200`, and `isConfigured: false`.

2. **Fill in the rest, then enable.**

   ```json
   { "enabled": true, "provider": "Twilio",
     "providerSettings": { "accountSid": "AC…", "authToken": "…", "serviceSid": "…" } }
   ```
   → `200` with `enabled: true`, `isConfigured: true`.

**Enabling an incomplete configuration is rejected** with `400`:

```json
{ "error": { "code": "VALIDATION_PROVIDERSETTINGS",
  "message": "Provider settings must contain only the setting names the selected provider supports, and every required one of them." } }
```

Nothing is written, so a store cannot half-enable a gateway and believe it is sending. Surface this
as a form-level error and highlight the fields listed in `selectableProviders` terms below.

#### Required settings per provider

| Provider | Required | Optional |
|----------|----------|----------|
| `2Factor` | `apiKey` | `senderId`, `baseUrl`, `sendPath`, `otpVariableName`, `expiryVariableName` |
| `Free2SMS` | `apiKey`, `senderId` | `baseUrl`, `route` |
| `Twilio` | `accountSid`, `authToken`, `serviceSid` | `messagingServiceSid`, `baseUrl` |

Setting names are matched case-insensitively, so `apikey` and `ApiKey` both work. Unknown names are
rejected rather than silently stored.

> Values are write-only. They are encrypted at rest and are never returned by any endpoint. To
> change one, submit a new value; to keep the existing one, omit that key.

#### UI states to render

| Condition | What to show |
|-----------|--------------|
| `enabled: false` | "SMS is off. Customers cannot receive codes." |
| `enabled: true`, `isConfigured: false` | "SMS is on but not ready" + list `missingSettings` |
| `enabled: true`, `isConfigured: true` | "SMS is active on *{provider}*", green |
| `requireVerifiedPhoneAtCheckout: "True"` | Info: "Customers must verify a phone number before checkout." |

Changing provider while `enabled: true` requires that provider's full settings in the same request —
the backend will not silently keep old credentials for a new gateway.

### Template setup (optional, separate screen)

`/admin/sms/templates` manages the message body per provider. Only one active template per provider
is permitted. The backend has a sensible default body when no template is registered, so a new
deployment does not need this screen before going live.

---

## Part 2 — What happens after configuration

This is the customer journey. **Every endpoint below requires `Authorization: Bearer <token>`.**

```
Admin enables SMS  ──▶  Storefront runs, SMS goes out automatically
```

There is no per-customer opt-in list and no activation step. The moment `isConfigured` is true, the
OTP endpoints work.

### The three OTP purposes

| Purpose | Used by | Phone changed? |
|---------|---------|----------------|
| `PhoneVerification` | Proving a number on the account | **Yes** — the only one |
| `Login` | Step-up during a session | No |
| `PasswordReset` | Confirming a number during a reset | No |

A `Login` or `PasswordReset` code never rewrites the account phone. Only `PhoneVerification` may,
and only when the code's number still matches the number currently being verified.

### Journey A — Register and verify (first-time)

1. `POST /api/v1/auth/register` with `phoneNumber`.
   The phone is stored **unverified**.
2. `POST /api/v1/otp/send`

   ```json
   { "phoneNumber": "+91 98765 43210", "purpose": "PhoneVerification" }
   ```

   ```json
   { "expiresAtUtc": "2026-10-01T12:30:00Z",
     "resendAvailableAtUtc": "2026-10-01T12:01:00Z" }
   ```

   The response carries **timings only — never the code**. Use `resendAvailableAtUtc` to drive the
   countdown instead of a client-side timer, so the button state always matches the server's.

   Phone numbers are canonicalised server-side, so `9876543210`, `+919876543210` and
   `+91 98765 43210` all resolve to the same account.
3. `POST /api/v1/otp/verify`

   ```json
   { "phoneNumber": "+91 98765 43210", "otp": "1234", "purpose": "PhoneVerification" }
   ```

   The user is taken from the bearer token — there is no `userId` field, and none should be sent.
   Success returns **`204 No Content`**.
4. `GET /api/v1/otp/verification-status` → `verified: true`.

### Journey B — Add or change a phone number

Replacing the number on a profile is deliberately **two-step**:

1. `PUT /api/v1/me/profile` with the new number.
   The number is staged as `pendingPhoneNumber`. `phoneNumber` still reports the old verified
   number, and `phoneNumberVerified` stays `true`.
2. `POST /api/v1/otp/send` with `purpose: "PhoneVerification"`.
3. `POST /api/v1/otp/verify` as above → promotes `pendingPhoneNumber` to `phoneNumber`.

**Read `pendingPhoneNumber` from the profile response.** When it is set, render the number as
"pending verification" instead of showing the old one as though the change were already live. Never
treat `phoneNumber` alone as proof that a requested change took effect.

If the customer requests a *second* change before verifying the first, the earlier code is refused
with `PHONE_VERIFICATION_NOT_PENDING` (409) and the pending number is left alone. Ask for a fresh
code.

### Journey C — Step-up login

`POST /api/v1/otp/send` with `purpose: "Login"`, then `/otp/verify` with the same purpose. The
code is spent and the session proceeds. The profile phone is not modified.

### What the customer sees at checkout

`GET /api/v1/otp/verification-status` is the single source of truth — derive every gate from
it rather than from a client-side flag:

```json
{ "verificationRequired": true,
  "phoneNumber": null,
  "verified": false,
  "pendingPhoneNumber": "+919876543210",
  "verificationSatisfied": false,
  "otpLength": 4,
  "otpExpiryMinutes": 10,
  "resendCooldownSeconds": 60 }
```

| Field | Meaning |
|-------|---------|
| `verificationRequired` | SMS is on **and** configured **and** store policy demands a verified phone |
| `phoneNumber` | The verified number on the account, canonical E.164, or `null` |
| `verified` | That number has been proven |
| `pendingPhoneNumber` | A number submitted for change but not yet verified, or `null` |
| `verificationSatisfied` | **Gate the checkout button on this one field** |
| `otpLength` | Digit count, so the input renders correctly (currently 4) |

`verificationRequired` is `true` only when SMS is enabled **and** configured. Turning SMS off
removes the checkout requirement entirely.

### Frontend rules

| Situation | Show |
|-----------|------|
| `verificationSatisfied: true` | Nothing. Checkout proceeds. |
| `verificationRequired: true`, `verified: false` | Verification prompt before checkout |
| `pendingPhoneNumber` non-null | Render it as "pending verification", not as the account number |
| Resend inside the cooldown | Neutral note, not an error: "You can request another code shortly." |
| `OTP_COOLDOWN` (409) | Same as above — also returned when a send is already in flight |
| `PHONE_VERIFICATION_NOT_PENDING` (409) | "This code is for a number you are no longer verifying." Request a new code |
| `OTP_INVALID` / `OTP_MAX_ATTEMPTS` (400) | Real error |
| `INVALID_PHONE_NUMBER` (400) | Inline field error |
| `SMS_NOT_CONFIGURED` / `OTP_SEND_FAILED` (500) | "We could not send a code. Try again shortly." Never echo provider detail |

Never display, log, or store the OTP outside the verification form. The storefront is never told which
gateway is in use — that is admin-only information.