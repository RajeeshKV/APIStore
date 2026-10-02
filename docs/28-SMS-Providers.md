# SMS Providers & OTP Verification

How OTP delivery is wired, which gateways are supported, how one is selected, how message
templates are managed, and what the backend enforces around phone verification.

---

## 1. Supported providers

Three integrations are supported. **Exactly one may be active at a time.**

| Provider   | `Sms:Provider` | Transport                                  | Auth                                    |
| ---------- | -------------- | ------------------------------------------ | --------------------------------------- |
| 2Factor    | `2Factor`      | HTTP `POST`, JSON                          | API key in the request body             |
| Free2SMS   | `Free2SMS`    | HTTP `POST`, JSON                          | `Authorization: Bearer <key>`           |
| Twilio     | `Twilio`      | HTTP `POST`, form-encoded, Verify v2       | HTTP Basic (`AccountSid:AuthToken`)     |
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

### Provider notes

**Twilio Verify** — <https://www.twilio.com/docs/verify/api/verification>

Verified against the official API reference.

```
POST {BaseUrl}/Services/{ServiceSid}/Verifications
Authorization: Basic base64({AccountSid}:{AuthToken})
Content-Type: application/x-www-form-urlencoded

  To={E164}                      required
  Channel=sms                    required
  CustomCode={OTP}               set by this adapter
  TemplateSid={HJ...}            when a Verify template is managed
  TemplateCustomSubstitutions={…} when that template also supplies a body
  MessagingServiceSid={MG...}    optional, needed to route to a real sender
```

Because `CustomCode` is sent, Twilio's own `VerificationCheck` endpoint is **not** used — this
application verifies the code itself. Twilio documents `20404` (destination unverified) as
retryable, since a different destination number would succeed; every other documented 4xx is
not retried. Numbers must be international; a bare national number is refused rather than
assumed to be a particular country.

**Free2SMS** — <https://free2sms.com/api.html> (REST API v1.0)

Verified against the official API reference.

```
POST {BaseUrl}/send
Authorization: Bearer {ApiKey}
Content-Type: application/json

  { "numbers": "9876543210", "message": "…", "sender_id": "F2SMS", "route": "otp",
    "template_id": 1207161234567890123 }
```

India numbers only — the API takes a bare 10-digit national number, so anything else is
refused before a request is made. Documented errors arrive at `details.code` for
application-level failures but at the top level for rate limiting and infrastructure failures,
so both are read. A rejected send is never billed, which makes retrying after a 4xx safe.

**2Factor** — <https://2factor.in>

> **Contract verification status — read before go-live.**
>
> 2Factor publishes **more than one generation** of this API and its own pages disagree on the
> details. Its machine-readable reference (`2fa.api-docs.io`) is a JavaScript-rendered Stoplight
> workspace and `docs-dev.2factor.in` answers 403, so neither could be read and quoted. The shapes
> that *were* readable on 2Factor's own product pages are:
>
> | Route | Shape |
> |-------|-------|
> | `POST /API/V1/OTP/SEND` | Header `X-API-Key`; body `{ to, channel, template_name, var1 }`; answers `{ "status": "sent", "session_id": "…" }` |
> | `POST /sms/{apiKey}/{TEMPLATE_NAME}` | Template **name is a URL segment** |
> | `POST /API/V1/{api_key}/SMS/{phone}/{otp}` | Legacy; sends our code, no template |
>
> The published field names for the template are **not** consistent — `template_name` in one
> example, `template` in another, `templateName` in 2Factor's own JavaScript SDK. Because of that,
> **every divergent name is configuration rather than a constant**:
>
> ```
> Sms__TwoFactor__BaseUrl
> Sms__TwoFactor__OtpPath             # native OTP route, default /API/V1/OTP/SEND
> Sms__TwoFactor__TransactionalPath   # fallback, supports {apiKey} and {template}
> Sms__TwoFactor__ApiKeyHeader        # X-API-Key; set empty to send apiKey in the body
> Sms__TwoFactor__TemplateNameField   # template_name
> Sms__TwoFactor__OtpVariableName     # var1
> Sms__TwoFactor__ExpiryVariableName  # var2
> Sms__TwoFactor__Channel             # SMS
> Sms__TwoFactor__DeliveryMode        # Auto | NativeOtp | TransactionalTemplate
> ```
>
> Confirm these against the API reference included with your 2Factor account and correct them
> without a rebuild. A wrong path surfaces as a normal, reported send failure; it can never take the
> API down or expose the API key. Treat the Twilio and Free2SMS contracts as confirmed and this one
> as unconfirmed until a real key has been used against the live account.

**2Factor delivery.** The native OTP route is attempted first. The transactional fallback — where
the template **name is part of the URL** — is used only when 2Factor reports the native route is
unavailable (`404`/`405`/`501`). It is deliberately *not* used after a timeout or `5xx`, because
those are ambiguous about whether the first SMS was delivered and a second send could leave the
customer holding one of two codes.

The **native route does not require a registered template**, so 2Factor works before anyone has
completed DLT registration. The transactional route does require one and fails with
`TEMPLATE_NOT_CONFIGURED` rather than sending an unresolved request.

2Factor has historically reported application-level failures as HTTP 200 with a `Status` field, so
an explicit non-success status is treated as a failure even on a 2xx, while a 2xx with no status
field is accepted.

---

## 2. Templates

Every supported gateway needs a pre-registered message. Templates are therefore stored in the
database (`sms_templates`) rather than in configuration: the registrations live in the vendor
portal, are per-deployment, and must be editable by a store owner who has no access to
environment variables.

| Field                | Purpose                                                                    |
| -------------------- | -------------------------------------------------------------------------- |
| `Provider`           | The gateway this template is registered with. Immutable after creation.     |
| `Name`               | Admin-facing label.                                                         |
| `Body`               | Message text containing placeholder tokens. Optional.                       |
| `ExternalTemplateId` | Vendor registration ID — Twilio `TemplateSid` (`HJ…`), Free2SMS DLT ID, 2Factor template name. |
| `IsActive`           | At most one active template per provider.                                  |

**Templates are optional.** A provider with no active template still sends, using its
documented default. That matters because deployments routinely go live before a DLT registration
has been approved.

### Placeholders

`{OTP}`, `{EXPIRY_MINUTES}`, `{STORE_NAME}`. Substituted at send time.

The same vocabulary works across all three gateways. For Twilio, a template that has both a
template ID and a body is sent as `TemplateCustomSubstitutions` with the keys derived from the
placeholders in the body, so an administrator defines a template once rather than learning a
second, provider-specific syntax.

### Managing templates

`Admin → Integrations → SMS → Templates`:

| Endpoint                                       | Purpose                        |
| ---------------------------------------------- | ------------------------------ |
| `GET  /api/v1/admin/integrations/sms/providers`   | Selectable gateways + notes   |
| `GET  /api/v1/admin/integrations/sms/templates`   | All templates, active or not  |
| `POST /api/v1/admin/integrations/sms/templates`   | Create                        |
| `PUT  /api/v1/admin/integrations/sms/templates/{id}` | Edit / activate           |
| `DELETE /api/v1/admin/integrations/sms/templates/{id}` | Delete                    |

Activating a template demotes the incumbent for that provider, so a send never depends on which
row a query happened to return. A filtered unique index (`"IsActive" = true`) enforces the same
rule in the database.

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
    "ServiceSid": "VA…"
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

| Provider  | Required                                 | Optional |
| --------- | ---------------------------------------- | -------- |
| 2Factor   | `ApiKey`                                 | `DeliveryMode`, `SenderId`, `BaseUrl`, `OtpPath`, `TransactionalPath`, `ApiKeyHeader`, `TemplateNameField`, `Channel`, `OtpVariableName`, `ExpiryVariableName`, `SendPath` (legacy alias for `OtpPath`) |
| Free2SMS  | `ApiKey`, `SenderId`                     | `BaseUrl`, `Route`, `DeliveryMode` |
| Twilio    | `AccountSid`, `AuthToken`, `ServiceSid` | `DeliveryMode`, `MessagingServiceSid`, `SenderId`, `BaseUrl`, `MessagingBaseUrl`, `MessagingPath` |

`DeliveryMode` applies to every provider and takes `Auto` (native OTP first, transactional only when
the native route is unavailable), `NativeOtp`, or `TransactionalTemplate`. See
`docs/API-Reference.md` → *SMS Providers & Templates* for the per-provider field reference.

When `enabled` is false, only the setting *names* are checked — switching SMS off must not
require re-supplying credentials that were never saved.

The runtime `Sms:Provider` in configuration still governs which adapter the factory hands out;
a saved selection for a different provider makes the other adapters refuse to send with
`PROVIDER_NOT_SELECTED`, so switching provider in the admin screen cannot leave the previous
provider's credentials in play.

---

## 4. OTP policy

Defaults, all configurable under `Sms:Otp` and clamped on read:

| Setting                | Default | Range / clamp                |
| ---------------------- | ------- | ---------------------------- |
| `ExpiryMinutes`        | 10      | 1–60                         |
| `ResendCooldownSeconds`| 60      | ≥ 0                          |
| `MaxAttempts`          | 5       | 1–10                         |
| `Length`               | **4**   | 4–10                         |

Codes are **four digits**. The default is declared once in `SmsOtpDefaults` and referenced from
both `SmsOtpPolicyOptions` and `SmsPolicyOptions`, because the two options types otherwise drift
and a caller binding through `IOtpService` picks up the interface's default parameter value
rather than the implementation's.

`GET /api/v1/otp/phone-verification-status` returns `otpLength`, so a client can size its input
correctly without hard-coding it.

---

## 5. Endpoints

| Endpoint                                       | Purpose                                   |
| ---------------------------------------------- | ----------------------------------------- |
| `POST /api/v1/otp/send`                         | Send an OTP                               |
| `POST /api/v1/otp/verify`                       | Verify an OTP                             |
| `GET  /api/v1/otp/phone-verification-status`    | Whether checkout is allowed to proceed    |
| `GET  /api/v1/admin/integrations/sms`           | Provider status, no secrets               |

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

1. Add a value to `SmsProviderKind` and to `SmsProviderKinds.Parse` / `ToName`.
2. Add a `*Options` class and a section on `SmsOptions`, with its required keys in
   `GetMissingSettings`.
3. Add its setting names to `SmsSettingNames`.
4. Implement `ISmsProvider` in `Infrastructure/Sms`.
5. Add a case to `SmsProviderFactory` and register its named `HttpClient`.

Nothing else needs to change: the admin provider list, the config validation and the status
projection all read from `SmsProviderKind`, which is what keeps them from drifting apart.
