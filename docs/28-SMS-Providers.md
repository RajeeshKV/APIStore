# SMS Providers & OTP Verification

How OTP delivery is wired, which gateways are supported, how one is selected, how message
templates are managed, and what the backend enforces around phone verification.

---

## 1. Supported providers

Three integrations are supported. **Exactly one may be active at a time.**

| Provider   | `Sms:Provider` | Transport                                  | Auth                                    |
| ---------- | -------------- | ------------------------------------------ | --------------------------------------- |
| 2Factor    | `2Factor`      | HTTP `POST`, JSON                          | API key in the `X-API-Key` header       |
| Free2SMS   | `Free2SMS`     | HTTP `POST`, JSON                          | `Authorization: Bearer <key>`           |
| Twilio     | `Twilio`       | HTTP `POST`, form-encoded (Messages API)   | HTTP Basic (`AccountSid:AuthToken`)     |
| *(none)*   | *(unset)*     | —                                          | —                                       |

The supported set is a closed list (`SmsProviderKinds.Selectable`). A removed integration
cannot be selected: the name is rejected by the admin config endpoint and by
`StoreAuthSettings`, and an unrecognised name in configuration degrades to a reported
"not configured" state rather than throwing.

> **Removed integrations.** TechTo Networks and SMSLocal were removed in this pass. Fast2SMS
> had already been removed — it called `GET /dev/bulkV2` with the API key in the query string
> and `route=otp`, which is not a documented Fast2SMS OTP contract, and mis-deserialised the
> provider's `message` field. Re-adding any of them should be done against the vendor's real
> API reference.

### We always own the code

Every adapter sends **the code this application generated**, never a code the vendor generated.

This is the central design decision, and it is deliberate. If the gateway generated the code,
verification would move to the vendor's API, and a customer would then face different attempt
limits, resend cooldowns, expiry and error messages depending on which gateway the store
happens to run. Provider selection exists to give a store owner a choice of *gateway*, not a
choice of *authentication semantics*.

The consequence is that `OtpService` remains the single source of truth for generation, hashing
(SHA-256), expiry, attempt ceilings and cooldown, and verification is a local hash comparison.

### Provider details

**Twilio Programmable Messaging** — <https://www.twilio.com/docs/sms/api/message-resource>

Uses the Messages API (not Verify). The OTP is placed in the message body.

```
POST https://api.twilio.com/2010-04-01/Accounts/{AccountSid}/Messages.json
Authorization: Basic base64({AccountSid}:{AuthToken})
Content-Type: application/x-www-form-urlencoded

  To={E164}          required (international format)
  From={FromNumber}  required (your Twilio phone number)
  Body=Your verification code is {OTP}. It expires in 10 minutes.
```

Numbers must be in E.164 format. A non-E.164 destination number is rejected before the request
is made. Each `2xx` response is a confirmed delivery; `20404` is the only 4xx treated as
retryable (a transient routing issue), all other 4xx are not retried.

**Free2SMS** — <https://free2sms.com/api.html> (REST API v1.0)

```
POST https://free2sms.com/api/v1/send
Authorization: Bearer {ApiKey}
Content-Type: application/json

  { "numbers": "9876543210", "message": "…{{OTP}}…", "sender_id": "F2SMS", "route": "otp" }
```

India numbers only — the API takes a bare 10-digit national number, so anything else is
refused before a request is made. The `{{OTP}}` placeholder in the message template is
substituted with the generated code. Documented errors arrive at `details.code` for
application-level failures but at the top level for rate limiting and infrastructure failures,
so both are read. A rejected send is never billed, which makes retrying after a 4xx safe.

**2Factor** — <https://2factor.in>

Sends the code this application generates through 2Factor's dedicated OTP endpoint:

```
POST https://2factor.in/API/V1/OTP/SEND
X-API-Key: {ApiKey}
Content-Type: application/json

  { "to": "{mobile}", "channel": "SMS", "template_name": "LOGIN_OTP", "var1": "{OTP}" }
```

The `template_name` is the name registered in your 2Factor portal (not a URL — the template is
managed in the portal, not in the admin UI). The OTP is passed as `var1`. India numbers only.
2Factor has historically reported application-level failures as HTTP 200 with a `Status` field, so
an explicit non-success status is treated as a failure even on a 2xx.

**Removed integrations.** TechTo Networks and SMSLocal were removed in this pass. Fast2SMS
had already been removed. Re-adding any of them should be done against the vendor's real
API reference.

---

## 3. Selecting and configuring one provider

`PUT /api/v1/admin/integrations/sms`

```jsonc
{
  "enabled": true,
  "provider": "Twilio",
  "providerSettings": {
    "AccountSid": "AC…",
    "AuthToken": "…",
    "FromNumber": "+15550000000"
  }
}
```

The write is validated against the supported provider set and against the setting names that
apply to the selected provider, and it is **persisted** — the selection and each setting value
are stored encrypted in `sms_provider_configs`, per setting, and read back by the adapters at
send time.

> This endpoint previously did nothing except append an audit event. It accepted any provider
> name, never persisted the selection or the settings, and wrote the raw `providerSettings` —
> which carry API keys and auth tokens — straight into the event payload intended for audit
> records. It also meant the admin screen reported a configuration that had no effect on
> delivery. The event now records the setting *names* only.

**Settings resolution order** is: saved administrator selection, then configuration, per value.
Configuration therefore stays the fallback, so a deployment that has only ever set environment
variables keeps working untouched, and a partially completed admin form remains usable.

Accepted setting names, and which are required:

| Provider  | Required                                        | Optional |
| --------- | ----------------------------------------------- | -------- |
| 2Factor   | `ApiKey`, `TemplateName`                        | —        |
| Free2SMS  | `ApiKey`, `SenderId`, `MessageTemplate`         | —        |
| Twilio    | `AccountSid`, `AuthToken`, `FromNumber`         | —        |

`TemplateName` (2Factor) is the name registered in your 2Factor portal. `MessageTemplate`
(Free2SMS) is the message body containing `{{OTP}}` as a placeholder for the generated code.
Twilio has no template field — the OTP is placed directly in the message body.

When `enabled` is false, only the setting *names* are checked — switching SMS off must not
require re-supplying credentials that were never saved.

The runtime `Sms:Provider` in configuration still governs which adapter the factory hands out;
a saved selection for a different provider makes the other adapters refuse to send with
`SMS_NOT_CONFIGURED`, so switching provider in the admin screen cannot leave the previous
provider's credentials in play.

### Getting the field catalogue

`GET /api/v1/admin/integrations/sms/providers`

Returns every selectable gateway with the exact fields its configuration form must render, the
required subset, and per-field metadata (type, max length, description). The same table that
drives this endpoint validates the save, so the rendered form and the enforced schema cannot
drift apart.

---

## 4. OTP policy

Fixed backend defaults. Not configurable:

| Setting                | Default |
| ---------------------- | ------- |
| `ExpiryMinutes`        | 5       |
| `ResendCooldownSeconds`| 60      |
| `MaxAttempts`          | 5       |
| `Length`               | 4       |

Codes are **four digits**. The default is declared once in `SmsOtpDefaults` and referenced from
`SmsPolicyOptions`. `GET /api/v1/otp/verification-status` returns `otpExpiryMinutes`,
`resendCooldownSeconds`, and `otpLength`, so a client can size its input and render timers
correctly without hard-coding.

---

## 5. Endpoints

| Endpoint                                       | Purpose                                   |
| ---------------------------------------------- | ----------------------------------------- |
| `POST /api/v1/otp/send`                         | Send an OTP                               |
| `POST /api/v1/otp/verify`                       | Verify an OTP                             |
| `GET  /api/v1/otp/verification-status`          | Whether checkout is allowed to proceed    |
| `GET  /api/v1/admin/integrations/sms`           | Provider status, no secrets               |
| `GET  /api/v1/admin/integrations/sms/providers` | Gateway catalogue + field schemas         |
| `PUT  /api/v1/admin/integrations/sms`           | Configure the active provider             |

### Send

Delivery is attempted **before** the OTP is persisted. Persisting first would leave an
unverifiable code in the table when the gateway refuses, consuming a cooldown window the
customer never used. A refused send writes no row and returns a business error.

A send is only reported as successful when the provider confirms acceptance. The no-op stand-in
returns `SMS_NOT_CONFIGURED` rather than pretending a code was sent.

### Phone verification and checkout

When `RequireVerifiedPhoneAtCheckout` is true **and** a provider is fully configured, checkout
rejects unverified customers and rejects any delivery address whose phone differs from the
verified one. With no provider configured the requirement is not enforced — requiring it would
lock every customer out of checkout.

Number handling: `SmsPhoneNumber` canonicalises to E.164 for storage and comparison, and each
adapter normalises to whatever its own gateway requires. A code sent to `+91 98765 43210` can
be verified by a client that echoes back `9876543210`.

---

## 6. Secrets

- Credentials live encrypted, one value per setting, in `sms_provider_configs`.
- Status endpoints report only *which* settings are present, never values.
- No adapter logs a credential, and no adapter logs the OTP. Verification failures log only a
  masked phone suffix.
- The audit event records setting **names**, not values.
- Credentials belonging to the inactive provider are never read.

---

## 7. Adding a provider

1. Add a value to `SmsProviderKind` and a case to `SmsProviderKinds.Parse` / `ToName`.
2. Add a `*Options` class and a section on `SmsOptions`, with its required keys in
   `GetMissingSettings`.
3. Add its setting names to `SmsSettingNames`.
4. Add a `SmsProviderSchema.For(...)` case describing which fields the admin UI renders.
5. Implement `ISmsProvider` in `Infrastructure/Sms`.
6. Add a case to `SmsProviderFactory` and register its named `HttpClient`.

Nothing else needs to change: the admin provider list, the config validation and the status
projection all read from `SmsProviderKind`, which is what keeps them from drifting apart.
