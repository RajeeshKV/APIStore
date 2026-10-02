# SMS / OTP Service — UI Flow & Front-End Integration Guide

Audience: front-end engineers building the storefront (and any future channel that verifies a
customer by SMS).

Backend reference: `docs/28-SMS-Providers.md` (gateway contracts, provider selection, template
management). This document covers only what a client needs.

---

## 0. Quick reference

| Need | Call |
| --- | --- |
| Should I show phone verification at all? | `GET /api/v1/store/settings` → `auth.mobileOtpEnabled` |
| Is this customer allowed to check out? | `GET /api/v1/otp/verification-status` → `verificationSatisfied` |
| Send a code | `POST /api/v1/otp/send` |
| Submit a code | `POST /api/v1/otp/verify` (204 on success) |

Codes are **4 digits**. Never hard-code that — read `otpLength` from the verification-status
response.

---

## 1. What the service actually does

Three things a client must internalise before designing screens:

1. **The application generates the OTP.** No gateway does. The code is a random 4-digit string,
   stored only as a SHA-256 hash. Consequences: the code is not recoverable from any API, and
   verification is a server-side hash comparison, so there is no "lookup what was sent" endpoint.
2. **The number must be Indian.** Input is canonicalised to `+91` + a 10-digit mobile number
   beginning 6–9, *before* any gateway is called. A number outside that shape is rejected with
   `INVALID_PHONE_NUMBER`. Do not build multi-country number entry.
3. **Delivery is attempted before the code is persisted.** A failed send writes no row and
   consumes no cooldown window, so a retry after a gateway error behaves as a first attempt.

`OtpPurpose` scopes a code. A code sent for one purpose cannot be used for another:

| Purpose | Use for |
| --- | --- |
| `PhoneVerification` | Proving control of the number on the account. The common case. |
| `Login` | Step-up during a session. |
| `PasswordReset` | Confirming the number during a reset. |

Only `PhoneVerification` changes the account phone, and only when the code's number still matches
the number being verified. A `Login` or `PasswordReset` code is spent and its own operation
proceeds, but it never rewrites the profile phone — proving control of a number for one operation
is not a decision to make it the account number.

---

## 2. User journey

### 2.1 Primary journey — verifying a phone before checkout

This is the flow with backend enforcement. It is not optional when the store policy requires it.

```
┌─────────────┐
│  Checkout    │  customer submits an order
└──────┬──────┘
       │ POST /api/v1/checkout
       ▼
┌──────────────────────────────────────────────────────────┐
│ 400 PHONE_VERIFICATION_REQUIRED                           │
│      → phone not verified                                  │
│ 400 ADDRESS_PHONE_MISMATCH                                │
│      → delivery phone ≠ verified phone                    │
└──────┬───────────────────────────────────────────────────┘
       │
       │ GET /api/v1/otp/verification-status
       ▼
┌──────────────────────────────────────────────────────────┐
│ verificationSatisfied: false                              │
│ → present the verification screen (see §2.2)              │
└──────┬───────────────────────────────────────────────────┘
       │ on success
       ▼
┌──────────────────────────────────────────────────────────┐
│ → refetch verification-status, then retry checkout        │
│   (do not optimistically re-enable the button)            │
└──────────────────────────────────────────────────────────┘
```

### 2.2 The verification screen — states

A single screen with a phone field and a code field. Walk the states in order.

#### State A — number entry

```
┌──────────────────────────────────────────┐
│  Verify your mobile number               │
│                                          │
│  We will send a 4-digit code to verify   │
│  your number.                            │
│                                          │
│  Mobile number                           │
│  [ +91  98765 43210                 ]    │
│                                          │
│              [ Send code ]               │
└──────────────────────────────────────────┘
```

- `+91` should be a fixed, non-editable prefix. It is not user-selectable today.
- Input mode: `inputmode="numeric"`, `autocomplete="tel"`.
- Pre-fill with `verificationStatus.phoneNumber` when present and already verified, and disable
  the field with a "Change number" affordance. Verification is bound to the account on success,
  so changing the number after verification invalidates the checkout gate until re-verified.
- **Disable "Send code" until the local number check passes.** This is an optimisation only — the
  server re-validates and this never replaces server-side checks.

#### State B — sending (loading)

```
┌──────────────────────────────────────────┐
│  Mobile number                           │
│  [ +91  98765 43210                 ]    │
│                                          │
│           [ ⟳ Sending code… ]            │
└──────────────────────────────────────────┘
```

- Disable **both** buttons. Disable the whole form — a second tap must not produce a second
  request.
- Do not optimistically advance to State C. The server may refuse (cooldown, rate limit,
  gateway failure). Advancing early shows a code field for a code that was never sent.

#### State C — code entry (counting down to resend)

```
┌──────────────────────────────────────────┐
│  Enter the code                          │
│                                          │
│  Sent to +91 98765 43210                 │
│                                          │
│      [ 4 ] [ 8 ] [ 2 ] [ 9 ]            │
│                                          │
│  Code expires in 09:42                   │
│                                          │
│  Didn't get it?  Resend in 00:37         │
└──────────────────────────────────────────┘
```

- Show the **masked** number (`+91 98765 ••••10` or `...3210`), not the full number, once sent.
- Start two timers from the send response:
  - **Expiry** → from `expiresAtUtc`
  - **Resend** → from `resendAvailableAtUtc`
- Derive the countdown from the server timestamps against the **local clock**, but keep a
  generous floor (e.g. `remaining = max(serverRemaining - elapsed, 0)`) so a client clock that is
  ahead cannot unlock resend early. The server is authoritative regardless — see §5.4.
- "Resend in 00:37" becomes an enabled "Resend code" at zero.
- Support paste. Most customers paste from the SMS rather than typing.
- Allow editing. Do not lock the input after N characters.

#### State D — verifying (loading)

Same treatment as State B. Disable inputs, show a spinner on the submit button.

#### State E — verified

```
┌──────────────────────────────────────────┐
│           ✓  Number verified             │
└──────────────────────────────────────────┘
```

- `POST /otp/verify` returns **204 with no body**. Success is the empty status code.
- Refetch `verification-status` before enabling a gated action (§2.1).
- Do **not** treat a 2xx with a body as success — there is no success body.

#### State F — error states

All errors share one envelope (§4). Render inline, keep the customer's input, never navigate away.

| Backend code | HTTP | UI treatment |
| --- | --- | --- |
| `VALIDATION_SUBMITTEDOTP` | 400 | Inline field error on the code box: "Enter the 4-digit code." Focus the input. |
| `VALIDATION_PHONENUMBER` | 400 | Field error on the phone box: "Enter a valid mobile number." |
| `OTP_INVALID` | 400 | Inline: "That code is not correct. Check it and try again." Clear the code box, keep the number. |
| `OTP_MAX_ATTEMPTS` | 400 | Inline: "Too many incorrect attempts. Request a new code." Force the resend affordance, disable submit until a new code is sent. |
| `INVALID_PHONE_NUMBER` | 400 | Field error on the phone box: "Enter a valid 10-digit mobile number." Return to State A. |
| `OTP_COOLDOWN` | 409 | Neutral banner, not an error: "You can request another code shortly." Start/refresh the resend countdown. Also returned when a send is already in flight for this number and purpose, so treat it as the same "wait" outcome. |
| `PHONE_VERIFICATION_NOT_PENDING` | 409 | Neutral banner: "This code is for a number you are no longer verifying. Request a new code." The customer changed the phone number again after this code was sent, so it is refused and no phone state moves. Send a fresh code for the number now pending. |
| `OTP_SEND_FAILED` | 500 | Banner: "We couldn't send the code. Try again." Keep the resend button enabled — a send failure consumes no cooldown. |
| `SMS_NOT_CONFIGURED` | 500 | Banner: "Verification codes are unavailable right now. Please contact support." Disable the flow. |
| *(rate limited)* | 429 | Banner: "Too many attempts. Try again shortly." Disable both actions for the window. |

> **Provider errors are deliberately collapsed.** The gateway adapters return specific codes
> (`MISSING_CREDENTIALS`, `MISSING_TEMPLATE`, Free2SMS
> `TEMPLATE_MISMATCH`, Twilio `20404`, …) and the send handler converts **every** one of them
> to `OTP_SEND_FAILED`. No vendor-specific code reaches the client, so there is nothing gateway-shaped
> to branch on and nothing about your infrastructure to leak. The specific provider error codes are
> documented in `docs/28-SMS-Providers.md`.

**Expired code is not a distinct error.** An expired code returns `OTP_INVALID`, because the
backend does not distinguish "wrong" from "expired" — deliberately, so the response cannot be used
to probe whether a code was ever issued. To render "expired", compare against the local expiry
timer you are already running; show the expiry copy when it has elapsed, and the generic invalid
copy otherwise.

### 2.3 Where else to trigger the flow

The same screen is reused, with a different `purpose`:

- **Account settings** — add or change the phone number on the profile. Note that changing a
  number is now a two-step flow: the backend stages a replacement in `pendingPhoneNumber` and only
  promotes it to `phoneNumber` once the OTP for the new number is verified. Read `pendingPhoneNumber`
  from the profile response and, when it is set, show the number as "pending verification" rather
  than displaying the old one as though the change were already live. The phone field on the profile
  still reports the currently verified number, so never treat it alone as proof that a requested
  change took effect.
- **Address book** — when creating or editing an address, the phone can be omitted; the backend
  fills it from the verified account number. Pre-fill it in the UI from
  `verificationStatus.phoneNumber` so what the customer sees matches what is stored.
- **Password reset** — `PasswordReset` purpose.

Do not gate sign-in on an OTP unless the store has also enabled mobile-OTP authentication. The
backend does not enforce it at login; adding a client-side-only gate would lock out customers who
have no verified number.

---

## 3. Decision: should the flow appear?

Use this ordering. Do not infer the rule client-side.

```
1. auth.mobileOtpEnabled === false
   → hide the whole flow. Offer phone entry only via account settings.

2. GET /api/v1/otp/verification-status   (authenticated)
   verificationSatisfied === true
   → no gate. Proceed.

3. verificationRequired === true && verified === false
   → show the verification screen. Checkout is blocked server-side until this passes.

4. verificationRequired === false && verified === false
   → verification is optional. Do not block; offer "Verify your number for faster checkout".
```

`auth` is a nested object on `GET /store/settings`:

```jsonc
{
  "businessName": "Kromic Store",
  "currencyCode": "INR",
  "auth": {
    "googleOAuthEnabled": false,
    "googleClientId": null,
    "emailPasswordEnabled": true,
    "mobileOtpEnabled": true     // ← the flag
  }
}
```

`verificationSatisfied` is the single flag to gate on. It is already `true` when verification is
not required, so it is safe to read unconditionally.

---

## 4. API reference

Base URL: `https://<api-host>/api/v1`. All request and response bodies are JSON.

### 4.1 Error envelope

Every non-2xx response from these endpoints has the same shape:

```json
{
  "success": false,
  "error": {
    "code": "OTP_INVALID",
    "message": "The OTP is invalid or expired."
  }
}
```

`code` is stable and safe to branch on. `message` is human-readable English intended for
display — it is fine to surface, but do not build logic on it, and prefer your own copy for
consistency with the rest of the storefront.

Status mapping:

| `error.code` prefix / type | HTTP |
| --- | --- |
| `VALIDATION_PHONENUMBER`, `VALIDATION_SUBMITTEDOTP`, `OTP_INVALID`, `OTP_MAX_ATTEMPTS`, `INVALID_PHONE_NUMBER` | 400 |
| `OTP_COOLDOWN`, `PHONE_VERIFICATION_NOT_PENDING` | 409 |
| rate limit | 429 |
| `SMS_NOT_CONFIGURED`, `OTP_SEND_FAILED` | 500 |
| `USER_NOT_FOUND` (verification-status) | 404 |
| unauthenticated (verification-status) | 401 |

`VALIDATION_*` codes are assembled at runtime as `VALIDATION_` + the offending field name in upper
case, so the full set for these endpoints is exactly the two named above.

### 4.2 `POST /otp/send`

Rate limited: **5 requests per 60 seconds per IP**.

Request:

```json
{
  "phoneNumber": "9876543210",
  "purpose": "PhoneVerification"
}
```

| Field | Type | Rules |
| --- | --- | --- |
| `phoneNumber` | string | Required. Regex `^\+?[1-9]\d{6,14}$`. Accepts `9876543210`, `+919876543210`, `+91 98765 43210`. Non-Indian numbers fail `INVALID_PHONE_NUMBER`. |
| `purpose` | enum | Required. `PhoneVerification` \| `Login` \| `PasswordReset`. |

Response `200`:

```json
{
  "expiresAtUtc": "2026-10-01T03:36:38.0000000Z",
  "resendAvailableAtUtc": "2026-10-01T03:22:38.0000000Z"
}
```

Both timestamps are the **only** timing information available. Drive both timers from them. There
is no field for the code length or the policy — read that from verification-status, or use the
documented default of 4.

Note: authentication is **optional**. The endpoint works for anonymous callers, which is what
allows pre-login verification. When the request is authenticated, a successful verification binds
the number to that account.

### 4.3 `POST /otp/verify`

Rate limited: **5 requests per 60 seconds per IP**. Shares the send window's budget — they are
the same rate-limit policy, so repeated verification attempts consume the same bucket as resends.

Request:

```json
{
  "phoneNumber": "9876543210",
  "otp": "4829",
  "purpose": "PhoneVerification"
}
```

| Field | Type | Rules |
| --- | --- | --- |
| `phoneNumber` | string | Required. Must canonicalise to the same value used at send. |
| `otp` | string | Required. Must be 4–10 digits (default codes are 4). |
| `purpose` | enum | Must match the purpose used at send. |

Response: **204 No Content, empty body.**

On success while authenticated, the backend sets the account's phone number and marks it
verified. Do not attempt to parse a success body.

### 4.4 `GET /otp/verification-status`

**Requires authentication.** Rate limited by the general policy.

Response `200`:

```json
{
  "verificationRequired": true,
  "phoneNumber": "+919876543210",
  "verified": false,
  "verificationSatisfied": false,
  "otpLength": 4,
  "otpExpiryMinutes": 10,
  "resendCooldownSeconds": 60
}
```

| Field | Meaning |
| --- | --- |
| `verificationRequired` | Store policy requires a verified phone before checkout **and** a provider is configured. |
| `phoneNumber` | Canonical E.164 on the account, or `null`. |
| `verified` | That number has been proven to belong to this customer. |
| `verificationSatisfied` | **Gate on this.** Already `true` when verification is not required. |
| `otpLength` | Render the code input at this width. Currently 4. |
| `otpExpiryMinutes` | Display hint for the expiry copy. |
| `resendCooldownSeconds` | Display hint for the resend copy. |

There is deliberately no provider field on this endpoint. Which gateway the store runs is an
internal detail that exposes infrastructure and gives a customer nothing they can act on. If you
need it for a support screen, read it from the admin integration-status endpoint instead.

Errors: 401 unauthenticated, 404 `USER_NOT_FOUND`.

---

## 5. Integration guide

### 5.1 Required UI components

| Component | Notes |
| --- | --- |
| `PhoneNumberInput` | Fixed `+91` prefix. `inputmode="numeric"`. `autocomplete="tel"`. Formats as the user types (`98765 43210`) but **submits the raw 10 digits**. |
| `OtpCodeInput` | `inputmode="numeric"`. Render exactly `otpLength` boxes. `autocomplete="one-time-code"` on the first box so iOS/Android offer the SMS code. Support paste into any box. Digits only; strip spaces and dashes on input. |
| `ResendCountdown` | Driven by `resendAvailableAtUtc`. Disabled → enabled transition. |
| `ExpiryCountdown` | Driven by `expiresAtUtc`. |
| `InlineError` | Per-field errors, plus a banner variant for non-field errors. |
| `VerificationGate` | Wrapper that owns the send/verify lifecycle and the two countdowns. |

**`autocomplete="one-time-code"` is not optional on mobile.** It is the difference between the
customer typing a 4-digit code from memory and the OS filling it in. Apply it to a single
input — many boxes are fine, but only one element may carry it.

### 5.2 Client-side validation

Local validation is a UX affordance only. The server re-validates everything, and the server's
rules are the ones that matter.

```js
// Indian mobile number, matching what the backend accepts.
const NATIONAL_MOBILE = /^[6-9]\d{9}$/;

export function normalisePhone(input) {
  return input.replace(/\D/g, '').replace(/^91(?=\d{10}$)/, '');
}

export function isValidNationalMobile(input) {
  return NATIONAL_MOBILE.test(normalisePhone(input));
}
```

`isValidNationalMobile` mirrors the backend's `SmsPhoneNumber.TryToNational`: exactly 10 digits,
first digit 6–9. Mirroring the rule keeps the UI and the server in agreement; do not invent a
looser or stricter local rule, or the customer will hit a rejection the UI said was impossible.

Gate the code input on `otpLength` from verification-status, falling back to 4.

### 5.3 Error mapping

```js
const OTP_ERROR_MESSAGES = {
  VALIDATION_SUBMITTEDOTP:       { field: 'otp',    text: 'Enter the 4-digit code.' },
  VALIDATION_PHONENUMBER:       { field: 'phone',  text: 'Enter a valid mobile number.' },
  OTP_INVALID:                   { field: 'otp',    text: 'That code is not correct. Check it and try again.' },
  OTP_MAX_ATTEMPTS:              { field: 'otp',    text: 'Too many incorrect attempts. Request a new code.', lockSubmit: true },
  INVALID_PHONE_NUMBER:          { field: 'phone',  text: 'Enter a valid 10-digit mobile number.' },
  OTP_COOLDOWN:                  { tone: 'neutral', text: 'You can request another code shortly.' },
  OTP_SEND_FAILED:               { tone: 'error',   text: "We couldn't send the code. Try again." },
  SMS_NOT_CONFIGURED:            { tone: 'error',   text: 'Verification codes are unavailable right now. Please contact support.', disableFlow: true },
};

export function describeOtpError(status, body) {
  if (status === 429) {
    return { tone: 'error', text: 'Too many attempts. Try again shortly.', disableFlow: true };
  }
  return OTP_ERROR_MESSAGES[body?.error?.code] ?? {
    tone: 'error',
    text: 'Something went wrong. Please try again.',
  };
}
```

Branch on `error.code`, never on `error.message`.

`SMS_NOT_CONFIGURED` is a store misconfiguration, not a customer mistake. Present it as a
support message and disable the flow: telling a customer which vendor setting is missing leaks
your infrastructure and tells them nothing they can act on.

### 5.4 Session state

State that must survive a page reload, and where to hold it:

```js
// sessionStorage: cleared when the tab closes. Survives reload, dies with the journey.
{
  phone:    '+919876543210',
  purpose:  'PhoneVerification',
  sentAt:   '2026-10-01T03:16:38.000Z',
  expiresAt:'2026-10-01T03:26:38.000Z',
  resendAt: '2026-10-01T03:17:38.000Z'
}
```

Rules:

- **Never store the OTP** in any storage. It exists only in the customer's SMS and, transiently,
  in the input element. Do not log it.
- **Reconstruct countdowns from timestamps, not remaining seconds.** Store the absolute UTC
  instants from the send response and compute remaining against `Date.now()` on every render.
  Storing a remaining count means a backgrounded tab resumes with a stale value and lets a
  customer resend early.
- **Clamp to the server's answer.** After a resend, replace both timestamps wholesale with the new
  response. Never assume they moved monotonically.
- **Do not trust the client clock for enforcement.** A device clock set forward would unlock
  resend early locally; the backend rejects it with `OTP_COOLDOWN` anyway. Clamp so the UI
  disables the button, and handle the rejection gracefully.
- **Clear on completion, on purpose change, and on sign-out.** A stale entry lets a later screen
  show a countdown for a code that no longer exists.
- **One in-flight request per action.** Guard send and verify with a flag; double-taps produce
  duplicate requests that burn the rate-limit window.

### 5.5 Rate limiting

`POST /otp/send` and `POST /otp/verify` share one budget: **5 requests per 60 seconds, per IP**,
with no queue. Exceeding it returns 429.

Practical consequences:

- A resend is a real cost from that budget. Do not poll, retry automatically, or re-send on mount.
- Send exactly once per user action.
- Handle 429 by disabling both actions and re-enabling on a client-side timer. The framework sends
  `Retry-After`; prefer it when present and fall back to the 60-second window when it is not.
- Behind a shared NAT or corporate proxy, many customers share one IP bucket. A visibly throttled
  customer may simply be behind a busy gateway.

### 5.6 Checkout integration

```js
async function placeOrder(payload) {
  const status = await getVerificationStatus();

  if (status && !status.verificationSatisfied) {
    openVerificationFlow({ purpose: 'PhoneVerification', returnTo: 'checkout' });
    return;   // do not submit; the server would reject with PHONE_VERIFICATION_REQUIRED
  }

  const res = await post('/checkout', payload);

  if (res.status === 400 && res.body?.error?.code === 'PHONE_VERIFICATION_REQUIRED') {
    openVerificationFlow({ purpose: 'PhoneVerification', returnTo: 'checkout' });
    return;
  }

  if (res.status === 400 && res.body?.error?.code === 'ADDRESS_PHONE_MISMATCH') {
    // The delivery contact must equal the verified number. Do not let the customer "fix" this
    // by typing an arbitrary number — that defeats the point of the gate.
    highlightAddressPhone();
    return;
  }
  // …
}
```

`ADDRESS_PHONE_MISMATCH` means the address's `phone` differs from the verified account number.
The resolution is to pre-fill the address phone from `verificationStatus.phoneNumber`, not to ask
the customer to type something else.

The server enforces this independently of the UI. The gate is a UX affordance, not the control —
a client that skips every check above is still rejected at `POST /checkout`.

---

## 6. Known gaps and caveats

Read these before designing the screens - each one will otherwise look like a frontend bug.

1. **`otpLength` is only reachable by authenticated clients.**
   `GET /otp/verification-status` requires a JWT, but `POST /otp/send` does not. An anonymous
   pre-login flow therefore has no endpoint that tells it how many digits to render, and must
   assume 4. If OTP length ever becomes configurable per store, a pre-login screen would break
   silently. The fix belongs in the backend — either expose the policy on the public settings
   endpoint, or make verification-status anonymous and return no user-specific data.

2. **Only Indian mobile numbers can be verified.** Canonicalisation to `+91` plus a 10-digit
   number beginning 6-9 happens before any gateway is called, so this holds even when Twilio is
   active and Twilio itself would deliver internationally. Do not build country selection.

3. **Expired and incorrect codes are indistinguishable.** Both return `OTP_INVALID`. The UI must
   decide which message to show from its own expiry timer, and that timer can drift if the device
   clock changes mid-flow.

4. **Login-time OTP is not enforced server-side.** `OtpPurpose.Login` exists, but the login
   endpoint does not require a verified code. A client that gates sign-in on an OTP would lock out
   every customer with no verified number.

5. **Send and verify share one rate-limit bucket, keyed by IP only.** Five requests per 60
   seconds in total, and the bucket is not per phone number. A household behind one IP can exhaust
   the budget between them, and there is no server-side throttle specific to a targeted number.

6. **The send response carries no policy.** Only two timestamps. Expiry and cooldown copy must be
   rendered from `verification-status`, or from the constants in section 4.

---

## 7. Testing checklist

- [ ] Reject a non-Indian number client-side; confirm the server rejects it independently.
- [ ] Tapping "Send code" twice quickly produces exactly one request.
- [ ] A failed send leaves "Resend code" **enabled** (a gateway failure burns no cooldown).
- [ ] `OTP_COOLDOWN` starts/refreshes the countdown rather than showing an error banner only.
- [ ] Expiry timer reaching zero shows expiry copy; an early wrong code shows invalid copy.
- [ ] `OTP_MAX_ATTEMPTS` disables submit until a new code is sent.
- [ ] Reload mid-countdown restores the correct remaining time.
- [ ] Reloading with a client clock set an hour forward does not enable resend early.
- [ ] Pasting a code from the SMS works, including with spaces or a leading country code.
- [ ] A successful verify refetches verification-status before enabling checkout.
- [ ] `SMS_NOT_CONFIGURED` is shown as a support message and disables the flow.
- [ ] 6 rapid requests produce a handled 429, not an unhandled rejection.
- [ ] Nothing writes the OTP to storage, analytics, or the console.

---

## 8. Reference

| Concern | Source of truth |
| --- | --- |
| Code length, expiry, cooldown | `GET /otp/verification-status` — never hard-coded |
| Whether verification is required | `verificationSatisfied` — never inferred client-side |
| Code validity, expiry, attempts | Server only. There is no local equivalent. |
| Gateway contracts, provider config, templates | `docs/28-SMS-Providers.md` |
| Admin: provider selection and credentials | `GET /api/v1/admin/integrations/sms/providers`, `PUT /api/v1/admin/integrations/sms` |