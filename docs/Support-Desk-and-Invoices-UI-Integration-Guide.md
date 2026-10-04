# UI Integration Guide — Support Desk & Order Invoices

Covers two features: the **customer support desk** (threaded tickets, media, status workflow) and
**order invoices** (PDF tax invoice per order).

Read the status table first — one of these two is not built yet, and building UI against the wrong
one wastes your time.

---

## What changed for the UI

| Feature | Backend status | Can I build the UI? |
|---------|---------------|---------------------|
| Support desk — customer screens | ✅ **Built and live** | Yes, now |
| Support desk — admin screens | ✅ **Built and live** | Yes, now |
| Support desk — attachments | ✅ Built and live | Yes, now |
| Support settings screen | ✅ Built — **but invoice fields are about to be removed** | Yes, minus the invoice toggles |
| Order invoices — **everything** | ❌ **Not implemented.** Specified only | **No.** Do not build against the invoice endpoints. |

### ⚠️ Deprecated: ticket invoices — do not integrate

The backend currently exposes ticket-invoice endpoints. **They are being removed and replaced by
order-invoice endpoints with different routes, different triggers, and a different response shape.**

| Deprecated — do not build | Replacement |
|---|---|
| `GET /api/v1/tickets/{ticketId}/invoices` | `GET /api/v1/orders/{id}/invoices` |
| `GET /api/v1/tickets/invoices/{invoiceId}/download` | `GET /api/v1/orders/invoices/{invoiceId}/download` |
| `GET /api/v1/admin/support/invoices/{invoiceId}/download` | `GET /api/v1/admin/invoices/{invoiceId}/download` |
| `PUT /api/v1/admin/support/invoices/{invoiceId}/content` | `PUT /api/v1/admin/invoices/{invoiceId}/content` |
| `POST /api/v1/admin/support/invoices/{invoiceId}/retry` | `POST /api/v1/admin/invoices/{invoiceId}/retry` |
| `GET /api/v1/admin/support/invoice-templates` | `GET /api/v1/admin/invoices/templates` |
| `POST /resolve` body field `requestInvoice` | **removed entirely** — invoicing is driven by the order lifecycle |
| Support settings field `autoGenerateInvoiceOnResolve` | **removed entirely** |

Two further fields are being removed from responses: `tickets[].latestInvoiceId`,
`tickets[].latestInvoiceStatus`, `tickets[].invoices`, and `invoices[].content.ticketNumber` /
`ticketSubject`. **Do not surface any of these in your models** — build the ticket screens without
them and they will not need changing when the invoice work lands.

Part 2 of this guide documents the target contract so you can design against it, but it is a
specification, not a live API.

---

## Conventions you can rely on

These hold across the whole API and are worth centralising in your HTTP client.

| Concern | Rule |
|---|---|
| Base path | `/api/v1` (version is in the path, not a header) |
| Auth | `Authorization: Bearer <accessToken>` on everything except public routes |
| Property casing | `camelCase` |
| Enums | **Always strings**, never integers — `"Open"`, `"Urgent"`, `"Razorpay"`. Register a string enum in your client, do not map to numbers |
| Timestamps | ISO-8601 **UTC**, e.g. `2026-10-04T09:30:00Z`. Render in the customer's local zone; never re-interpret as local time |
| Money | `decimal` on the wire, rendered as a **number**. Format with `currencyCode` from the payload — never hardcode ₹ or $ |
| Uniqueness | `decimal` can arrive as a number **or a string**. Parse defensively |

### Error envelope

Domain and handler errors share one shape:

```json
{ "success": false, "error": { "code": "TICKET_NOT_OPEN", "message": "Only an open ticket can be resolved." } }
```

**There are two different `400` shapes, and your client must handle both.**

| Source | Shape | When |
|---|---|---|
| DataAnnotations / model binding (`[Required]`, `[StringLength]`) | ASP.NET `ValidationProblemDetails` | Body fails validation before the action runs |
| FluentValidation + domain rules | `{ success: false, error: { code, message } }` with `code = "VALIDATION_<FIELD>"` and **all** messages joined by `" \| "` | Body parsed, then rejected by a validator |

```jsonc
// Shape 1 — model binding
{ "title": "One or more validation errors occurred.", "status": 400,
  "errors": { "Subject": ["The Subject field is required."] } }

// Shape 2 — validator / domain
{ "success": false, "error": {
    "code": "VALIDATION_BODY",
    "message": "Comment body is required. | Comment nesting cannot exceed 6 levels." } }
```

A client that only reads `error.message` will show a blank error for shape 1. Normalise both into
one `{ code, message, fieldErrors? }` type in your HTTP layer, once.

### Pagination

`GET` list routes return:

```json
{ "items": [], "page": 1, "pageSize": 20, "totalCount": 0,
  "totalPages": 0, "hasNextPage": false, "hasPreviousPage": false }
```

`totalPages` is `0`, not `1`, when there are no results. Guard the pager against that.

---

# Part 1 — Support Desk

## 1. Customer — ticket list

`GET /api/v1/tickets?status=&page=&pageSize=`

Scoped to the caller from the token. No customer id is sent or read.

```
┌─ My support requests ────────────────────────────────────────────────────┐
│ [ All ▾ ]  [ 🔍 Search subject… ]                      TKT-2026-000123   │
├─────────────────────────────────────────────────────────────────────────┤
│ ● Open      My order never arrived          about order #4821    2 msgs   │
│              Last activity 3 hours ago                         [ Open → ]  │
├─────────────────────────────────────────────────────────────────────────┤
│ ● Resolved  Changed my mind on size         (no order)        1 msg      │
│              Auto-closes in 2 days                          [ Open → ]  │
└─────────────────────────────────────────────────────────────────────────┘
```

```jsonc
// GET /api/v1/tickets?page=1&pageSize=20
{
  "items": [{
    "id": "9f1c…", "ticketNumber": "TKT-2026-000123",
    "subject": "My order never arrived",
    "status": "Open",                 // Open | Resolved | Closed
    "priority": "Normal",             // Low | Normal | High | Urgent
    "customerId": "…", "customerName": "Ada Lovelace", "customerEmail": "ada@example.test",
    "assignedAdminId": null,
    "relatedOrderId": "3ab…", "orderNumber": "#4821",
    "commentCount": 2, "reopenCount": 0,
    "awaitingFirstResponse": true,
    "createdAtUtc": "2026-10-04T06:00:00Z",
    "lastActivityAtUtc": "2026-10-04T09:00:00Z",
    "resolvedAtUtc": null, "closedAtUtc": null,
    "autoCloseAtUtc": null
  }],
  "page": 1, "pageSize": 20, "totalCount": 1,
  "totalPages": 1, "hasNextPage": false, "hasPreviousPage": false
}
```

> `customerEmail` is present on the **customer's own** list. Harmless here, but do not log the
> payload — it is PII. Admin screens are the ones that genuinely need it.

**UI guidance**

| Field | Render as |
|---|---|
| `status` | Badge. `Open` blue, `Resolved` green, `Closed` grey |
| `awaitingFirstResponse` | A "we're on it" marker. Suppress it when `status != "Open"` |
| `autoCloseAtUtc` | On a `Resolved` ticket, show a relative countdown — "closes in 2 days" |
| `orderNumber` | Render as a link into the order detail page when non-null |
| `latestInvoiceId` / `latestInvoiceStatus` | **Omit.** Being removed |

`status` is optional. A value that is not one of the three names returns `400
INVALID_TICKET_STATUS` rather than being silently ignored — surface it as a bug in your filter, not
as an empty list.

## 2. Customer — new ticket

`POST /api/v1/tickets`

```jsonc
{ "subject": "My order never arrived",         // 4–200 chars, required
  "description": "Ten days and tracking has not updated.", // 4–8000 chars, required
  "orderId": "3ab…" }                          // optional, nullable
```

`201` returns the created `TicketSummaryResponse`.

Mirror the limits in the form so the user finds out before submitting:

| Rule | Mirror as |
|---|---|
| `subject` 4–200 | `minlength=4 maxlength=200`, live counter near 200 |
| `description` 4–8000 | Same |
| `orderId` optional | Order picker, prefilled from "my orders" when arriving from an order page |

`orderId` does **not** have to belong to the caller — verify server-side returns
`404 TICKET_ORDER_NOT_FOUND` for a foreign or unknown id.

## 3. Customer — ticket thread

`GET /api/v1/tickets/{ticketId}`

```
┌─ TKT-2026-000123 ────────────────────────────── ● Resolved ────────────┐
│ My order never arrived                          about order #4821       │
│ ────────────────────────────────────────────────────────────────────────│
│  Ada Lovelace · 4 Oct 06:00                                              │
│  Ten days and tracking has not updated.                                  │
│                                                                          │
│  ┌ Support Team · 4 Oct 08:30 ───────────────────────────────┐          │
│  │ Sorry about that — I've opened an investigation with the   │          │
│  │ courier and will update you within 24 hours.              │          │
│  └────────────────────────────────────────────────────────────┘         │
│    └─ reply ▾                                                             │
│                                                                          │
│  ┌ You · 4 Oct 09:00 ────────────────────────────────────────┐           │
│  │ Thank you, here is a photo of the box.                     │           │
│  │ [img] cracked-panel.jpg                                    │           │
│  └────────────────────────────────────────────────────────────┘           │
│ ────────────────────────────────────────────────────────────────────────│
│ [ Write a reply…                              ]  [ 📎 ] [ Send ]        │
│ This closes automatically in 2 days.        [ Confirm it's fixed ]        │
└──────────────────────────────────────────────────────────────────────────┘
```

The response is wrapped one level deeper than you would expect:

```jsonc
{
  "ticket": {                       // ← AdminTicketDetailResponse wraps the detail
    "id": "9f1c…", "ticketNumber": "TKT-2026-000123",
    "subject": "My order never arrived",
    "description": "Ten days and tracking has not updated.",
    "status": "Resolved", "priority": "Normal",
    "customerId": "…", "customerName": "Ada Lovelace", "customerEmail": "ada@example.test",
    "assignedAdminId": null,
    "relatedOrderId": "3ab…", "orderNumber": "#4821",
    "reopenCount": 0, "awaitingFirstResponse": false,
    "createdAtUtc": "2026-10-04T06:00:00Z",
    "lastActivityAtUtc": "2026-10-04T09:00:00Z",
    "resolvedAtUtc": "2026-10-04T08:30:00Z",
    "closedAtUtc": null,
    "autoCloseAtUtc": "2026-10-07T08:30:00Z",
    "comments": [ /* recursive — see below */ ],
    "history": [ /* visible transitions only — see below */ ]
  },
  "fullHistory": [ /* same list; admin-only */ ]
}
```

> `fullHistory` is identical to `history` today. It exists so internal transitions can be added to
> the admin view without changing the customer contract. **Read `history` on customer screens.**

### The comment tree is recursive

```jsonc
"comments": [{
  "id": "c1…", "parentCommentId": null,
  "authorId": "…", "authorName": "Support Team",
  "isAdminAuthor": true,
  "body": "Sorry about that…",
  "depth": 0,
  "isInternalNote": false,          // always false on a customer screen
  "createdAtUtc": "2026-10-04T08:30:00Z",
  "attachments": [],
  "replies": [{
    "id": "c2…", "parentCommentId": "c1…",
    "authorName": "Ada Lovelace", "isAdminAuthor": false,
    "body": "Thank you, here is a photo of the box.",
    "depth": 1, "isInternalNote": false,
    "createdAtUtc": "2026-10-04T09:00:00Z",
    "attachments": [{
      "id": "a1…", "kind": "Image",
      "publicId": "support/9f1c…/a1b2",
      "secureUrl": "https://res.cloudinary.com/…/a1b2.jpg",
      "format": "jpg", "contentType": "image/jpeg",
      "width": 1600, "height": 1200,
      "durationSeconds": null,      // video only
      "sizeBytes": 284119,
      "altText": "Cracked panel"
    }],
    "replies": []                    // recursion terminates here
  }]
}]
```

**The server already applies `depth` for you.** Nest by `replies`, and use `depth` only for
indentation if you want it. Do not re-derive nesting from `parentCommentId` — it is a forest with
many roots, so a naive parent walk gets this wrong.

Max nesting is **6 levels**; a 7th is rejected with `400`.

### Posting a reply

`POST /api/v1/tickets/{ticketId}/comments`

```jsonc
{
  "body": "Thank you, here is a photo of the box.",   // 1–10000 chars, required
  "parentCommentId": "c1…",                          // omit for a top-level message
  "attachments": [ /* from the upload step, see §4 */ ]
}
```

`201` returns the created `TicketCommentResponse` — the whole new subtree, so you can append it
directly without refetching.

### ⚠️ Replying to a closed ticket reopens it

`status: "Closed"` does **not** reject a new comment. The server reopens the ticket, increments
`reopenCount`, and writes a history row saying so.

| What you do | What the user sees |
|---|---|
| Hide the composer on a closed ticket | They can never get help again, and never learn why |
| Show it with a hint — "Reopen this request" — and optimistic-update `status` to `Open` | Matches the server |

The second option is the honest one. **Never optimistically set `Closed` on a reply.**

### History

```jsonc
"history": [{
  "id": "h1…", "fromStatus": null, "toStatus": "Open",
  "actor": "User",                  // User | Admin | System
  "actorId": "…", "actorName": "Ada Lovelace",
  "note": null,                     // present on resolved/reopened transitions
  "occurredAtUtc": "2026-10-04T06:00:00Z"
}]
```

`actor: "System"` means the idle worker closed it. Render that distinctly — "Closed automatically
after 3 days of inactivity" reads very differently from "closed by support", and the difference is
the whole reason the audit log records who.

## 4. Attachments

Two steps, deliberately. Upload first, then reference the result — so a comment is still writable
when the media provider is down.

### Step 1 — upload

`POST /api/v1/tickets/media` — `multipart/form-data`

| Part | Type | Notes |
|---|---|---|
| `file` | binary | required, non-empty |
| `altText` | text | optional, ≤ 500 chars |

`201`:

```jsonc
{ "kind": "Image", "publicId": "support/9f1c…/a1b2",
  "secureUrl": "https://res.cloudinary.com/…/a1b2.jpg",
  "format": "jpg", "contentType": "image/jpeg",
  "width": 1600, "height": 1200, "durationSeconds": null, "sizeBytes": 284119 }
```

Send `kind`, `publicId`, `secureUrl`, `format`, `contentType`, `width`, `height`,
`durationSeconds`, `sizeBytes` straight back in `attachments`.

### Limits — validate client-side, the server is strict

| Kind | Accepted `Content-Type` | Max size |
|---|---|---|
| Image | `image/jpeg`, `image/png`, `image/webp`, `image/gif`, `image/avif` | **10 MB** |
| Video | `video/mp4`, `video/webm`, `video/quicktime` | **25 MB** |

An allow-list, not a deny-list. Validate against it **before** uploading — a rejected upload is a
wasted round trip and a confusing error.

| Failure | Response | Show as |
|---|---|---|
| Wrong type | `400 INVALID_MIME_TYPE` | Inline, naming the allowed types |
| Too large | `413 FILE_TOO_LARGE` | Inline, with the limit |
| Provider down | `502 UPLOAD_FAILED` | Retryable — offer a retry, let them send the comment without it |
| More than 6 attachments | silently **truncated** | See below |

> **⚠️ Known defect — video size limit is inconsistent right now.** The upload endpoint's request
> limit is 50 MB but the domain enforces **25 MB**. A video between 25 and 50 MB uploads
> successfully and is then rejected when the comment is posted with `400 TICKET_COMMENT_INVALID`,
> after the user has already typed their message. Treat **25 MB** as the real limit until the
> backend is fixed.

> **⚠️ Attachments are silently truncated, not rejected.** The merchant's
> `maxAttachmentsPerComment` setting (0–6, default 6) caps the count, and anything over the cap is
> **dropped without an error**. If a user attaches 8 files to a setting of 6, the comment posts
> with 6 and they never learn about the other 2. Read `maxAttachmentsPerComment` from
> `GET /api/v1/admin/support/settings` and cap the picker at that number, with a counter, so the
> user can never reach the truncation.

### Rendering

| Kind | Render |
|---|---|
| `Image` | `<img>` from `secureUrl`, `alt` from `altText`, `loading="lazy"` |
| `Video` | `<video controls preload="none">`; show `durationSeconds` as a badge |

`width` / `height` are present so you can reserve layout space and avoid reflow. `secureUrl` is
what you display — never construct a Cloudinary URL yourself.

## 5. Customer — close and reopen

### Close — "confirm it's fixed"

`POST /api/v1/tickets/{ticketId}/close` — `{ "note": "Sorted, thanks!" }` (optional, ≤ 2000)

Only valid while `status === "Resolved"`. `409 TICKET_NOT_OPEN` otherwise.

Show the button **only** on a `Resolved` ticket. When the idle window elapses the server closes the
ticket itself and a late click returns `409` — handle it by refetching and showing "This request
was closed automatically", not as an error.

### Reopen

`POST /api/v1/tickets/{ticketId}/reopen` — `{ "reason": "Still not right" }` (optional, ≤ 2000)

Valid from `Resolved` **and** `Closed`. `200` with the updated summary. `reopenCount` increments —
show it as "Reopened 2×" once above 0, because a repeatedly-reopened ticket is a signal the
resolution did not hold.

## 6. Admin — support queue

`GET /api/v1/admin/tickets?status=&priority=&search=&unansweredOnly=&page=&pageSize=`

```
┌─ Support queue ──────────────────────────────────────────────────────────┐
│ [All ▾] [All ▾] [🔍 Search…] [☐ Unanswered only]                         │
├─────────────────────────────────────────────────────────────────────────┤
│ #  Customer        Subject            Order   Priority  Last activity  …  │
│ 1  Ada Lovelace    Never arrived      #4821   ●Urgent   3h ago     [Open] │
│ 2  Ben Ortiz       Wrong size                  ●Normal   1d ago     [Open] │
│ 3  Cara Singh      Refund status?              ●Low      2d ago     [Open] │
└─────────────────────────────────────────────────────────────────────────┘
```

`search` matches subject and customer name/email. An unrecognised `status` or `priority` is `400`
(`INVALID_TICKET_STATUS` / `INVALID_TICKET_PRIORITY`) — never silently ignored, because an ignored
filter looks exactly like an empty queue.

### Admin-only actions

| Method | Route | Body / query | Notes |
|---|---|---|---|
| POST | `/admin/tickets/{id}/comments?isInternalNote=true` | `PostTicketCommentRequest` | Note the flag is a **query** parameter |
| POST | `/admin/tickets/{id}/resolve` | `{ "resolutionNote": "…" }` | Admin only. Arms auto-close |
| POST | `/admin/tickets/{id}/priority` | `{ "priority": "Urgent" }` | Case-insensitive on input |
| POST | `/admin/tickets/{id}/assign` | — | Assigns to the caller |

`isInternalNote` is a **query string** flag, not a body field. Putting it in the body silently
creates a normal customer-visible reply.

There is deliberately **no** admin close or reopen route. Closure is the customer's confirmation
that the fix worked, so `Ticket.Close` rejects an administrator actor at the domain level. A call
to a non-existent route returns `404`, not `409`. Render close/reopen as customer-only actions, or
omit them from the admin UI entirely — but do not add them later "for convenience".

## 7. Admin — ticket detail

`GET /api/v1/admin/tickets/{ticketId}` — same wrapper as the customer view, with internal notes now
populated.

```
┌─ TKT-2026-000123 ────────────────────── ● Open ── ●Urgent ── [Assign me] ─┐
│ Ada Lovelace · ada@example.test · about order #4821                      │
│ ─────────────────────────────────────────────────────────────────────────│
│  Ada · 06:00 ─ Ten days and tracking has not updated.                    │
│  Support · 08:30 ─ Sorry, opened an investigation.                        │
│  Support · 09:15 ─ ⚙ INTERNAL  Customer mentioned a second box. Escalate.│
│  └─ reply · └─ reply as internal note                                     │
│ ─────────────────────────────────────────────────────────────────────────│
│ [ Resolve… ]  [ Priority ▾ ]                                              │
└──────────────────────────────────────────────────────────────────────────┘
```

**Internal notes are the security-critical part of this screen.**

| Rule | Consequence for the UI |
|---|---|
| Never returned to a customer | The customer screen has nothing to hide — you cannot leak it by forgetting a CSS rule |
| Never emailed to a customer | Confirmed by test |
| An internal note is visually distinct | Amber left border, a lock glyph, and the label "Internal note — not visible to customer". It is a colleague-facing message; misreading it as customer-facing is how a private remark gets typed out in public |
| A customer cannot reply under one | `404 TICKET_COMMENT_NOT_FOUND`, because answering would reveal it exists |
| `isInternalNote` on a comment you authored | Show the toggle **only** on your own comments |

When you render a reply composer on an internal note, default to **internal** and make the toggle
explicit. A customer reply typed into an internal thread posts publicly.

### Resolve

```
┌─ Resolve TKT-2026-000123 ─────────────────────┐
│ Resolution note (optional, shown to customer)  │
│ ┌───────────────────────────────────────────┐ │
│ │ Refund raised with the courier.           │ │
│ └───────────────────────────────────────────┘ │
│                                               │
│ The ticket closes automatically in 72 hours   │
│ unless the customer replies.                  │
│                              [ Cancel ] [ Resolve ] │
└───────────────────────────────────────────────┘
```

`POST /admin/tickets/{id}/resolve` returns `200` with the updated summary. **Resolving does not
create an invoice** — that moved to the order lifecycle. Do not add an "attach invoice" checkbox.

The resolution note is customer-visible. If it may contain internal reasoning, put that in an
internal note instead.

## 8. Admin — support settings

`GET /api/v1/admin/support/settings` · `PUT /api/v1/admin/support/settings`

```
┌─ Support settings ───────────────────────────────────────────────────────┐
│ Auto-close                                                               │
│   Close a resolved ticket after   [ 72 ] hours of customer silence       │
│                                                                          │
│ Notifications                                                            │
│   ☑ Email support when a ticket is opened                                │
│   ☑ Email support when a ticket is reopened                              │
│   ☑ Email the customer when a ticket is resolved                         │
│                                                                          │
│ Attachments                                                              │
│   Maximum attachments per comment    [ 6 ]                               │
│                                                                          │
│ Admin notifications                                                      │
│   ⓘ support@example.com  (configured via Support__AdminNotificationEmail)│
│                                                                          │
│                                                        [ Save changes ]  │
└──────────────────────────────────────────────────────────────────────────┘
```

```jsonc
// GET
{
  "autoCloseIdleHours": 72,
  "notifyAdminOnTicketCreated": true,
  "notifyAdminOnTicketReopened": true,
  "notifyCustomerOnTicketResolved": true,
  "maxAttachmentsPerComment": 6,
  "adminNotificationConfigured": true,
  "adminNotificationTarget": "su*****@example.com"   // masked, read-only
}
```

`adminNotificationTarget` is **either** a masked address **or the literal string
`"not configured"`** — never an empty string. Branch on `adminNotificationConfigured` rather than
testing the value for `@`, or you will render `"not configured"` as if it were an address.

The mask keeps the first two characters of the local part and replaces the rest with one `*` per
hidden character, so the length is stable and it cannot be used to guess an address.

**`PUT` is a partial update.** Every field is optional and `null` means *leave as is*:

```jsonc
{ "autoCloseIdleHours": 48, "maxAttachmentsPerComment": 4 }   // touches only these two
```

Omitting a field never resets it — so a form that saves one toggle cannot wipe the others.

| Field | Range | Notes |
|---|---|---|
| `autoCloseIdleHours` | 1–720 | Out of range → `400 IdleWindowOutOfRange` |
| `maxAttachmentsPerComment` | 0–6 | Must match what your attachment picker allows (§4) |
| `notify*` | booleans | |
| `adminNotificationTarget` | — | **Read-only and masked.** Not editable from any API |

The admin address is deployment-level (`Support__AdminNotificationEmail`) and is intentionally not
editable from the app — a storefront-facing setting that rewrites the destination of operational
mail is an open relay. Render it read-only. When `adminNotificationConfigured` is `false`, support
still works; only the internal alert is lost, and each affected ticket is logged.

> **Removed soon:** `automatedInvoiceMailingEnabled`, `autoGenerateInvoiceOnResolve`,
> `invoiceMailSubjectOverride`. Do not build these three toggles.

## 9. Refetch checklist

The backend has no push channel for tickets. Refetch after:

| Action | Refetch |
|---|---|
| Customer posts a comment | Thread + list row (`lastActivityAtUtc`, `commentCount`) |
| Admin resolves / closes / reopens | Thread + list row |
| Auto-close fires (any time after `autoCloseAtUtc`) | Thread + list row |
| Assign or priority change | List row |
| **Window regains focus** | List — an auto-close may have landed while the user was away |

That last one is the one teams miss. A customer staring at a `Resolved` ticket whose countdown
reached zero may be looking at a stale `Resolved` badge. Refetching on focus is cheap and prevents
a confusing "I clicked close and got a 409".

---

# Part 2 — Order Invoices (target contract — NOT built)

> **Nothing in Part 2 exists yet.** This is the design you can build against once the backend
> lands, and it is included so the ticket and order UIs can be designed together. Field names and
> routes are specified in
> [39 — Support Desk and Order Invoice Generation](39-Support-Desk-and-Invoice-Generation.md#part-b--order-invoice-generation)
> and may still change — but the *shape* will not: an invoice belongs to an order, is triggered by
> the order's money being committed, and is immutable once generated.

## 10. When an invoice appears

| Payment method | Appears when |
|---|---|
| Razorpay (prepaid) | Payment is captured |
| Cash on delivery | Order reaches **Delivered** |

COD issues at delivery rather than confirmation so a document never enters an "issued but unpaid"
state that would later need mutating. An administrator can also issue one manually for pro-forma
and delivery-challan needs.

**Practical consequence:** after a prepaid payment succeeds, poll
`GET /orders/{id}/invoices` when the customer opens their order page. Do not assume it is there on
the first render — generation is asynchronous. It will appear as `Pending` first, then `Generated`.

## 11. Customer — invoices on the order page

`GET /api/v1/orders/{id}/invoices`

```
┌─ Order #4821 ────────────────────────────── ● Delivered ────────────────┐
│ Placed 4 Oct · Razorpay · Paid 4 Oct 06:12                                  │
│                                                                           │
│ ┌─ Invoice ────────────────────────────────────────────────────────┐      │
│ │ INV-2026-000123          Rev 1        Generated 4 Oct 06:15       │      │
│ │ [ Download PDF ]                                                 │      │
│ │                                                                   │      │
│ │ Superseded revisions (collapsed)                                 │      │
│ │   ▸ INV-2026-000123-R2   Superseded          [ Download PDF ]    │      │
│ └───────────────────────────────────────────────────────────────────┘      │
└───────────────────────────────────────────────────────────────────────────┘
```

`OrderResponse` has **no** invoice field, so this is a second request. Fetch it in parallel with
the order detail, not chained after it.

Response shape:

```jsonc
{
  "current": {
    "id": "…", "invoiceNumber": "INV-2026-000123",
    "revision": 1, "kind": "Invoice",        // Invoice | CreditNote
    "status": "Generated",                   // Pending | Generated | Failed | Superseded
    "orderId": "3ab…", "orderNumber": "#4821",
    "templateId": "…", "isContentOverridden": false,
    "queuedAtUtc": "…", "generatedAtUtc": "…",
    "generationAttempts": 1, "failureReason": null,
    "fileName": "INV-2026-000123.pdf", "sizeBytes": 48210,
    "emailedToCustomer": true, "emailedAtUtc": "…",
    "hasDocument": true
  },
  "superseded": [ /* older revisions — retained, still downloadable */ ]
}
```

### Invoice UI rules

| Situation | What to render |
|---|---|
| `current.status === "Pending"` | "Preparing your invoice…" — a spinner, **no** download button |
| `current.status === "Generated"` | Download button |
| `current.status === "Failed"` | "We couldn't prepare your invoice. Our team has been notified." **No retry button** — that is admin-only |
| `current == null`, no `superseded` | Render nothing at all. Absence is normal, not an error |
| `kind === "CreditNote"` | Label as a credit note, not an invoice. Show the amount as the reversal, not as a payment |
| `hasDocument === false` | Hide the download button rather than offering one that 409s |

`current` is a single object and `superseded` is an array — a newer revision always wins, and the
older ones are kept because they may have been mailed and filed. Never merge them into one list:
showing two competing "the invoice" documents is how a customer disputes the wrong number.

Download: `GET /api/v1/orders/invoices/{invoiceId}/download` returns the PDF bytes. A foreign order
returns `404`, not `403`.

## 12. Admin — invoice management

| Method | Route | Purpose |
|---|---|---|
| POST | `/admin/orders/{id}/invoice/issue` | Queue manually. `201` new, `200` already invoiced |
| GET | `/admin/orders/{id}/invoices` | Full list |
| GET | `/admin/invoices/{invoiceId}` | One revision |
| GET | `/admin/invoices/{invoiceId}/download` | PDF |
| PUT | `/admin/invoices/{invoiceId}/content` | Edit wording — `Pending` only |
| POST | `/admin/invoices/{invoiceId}/retry` | Re-queue a `Failed` revision |
| GET/PUT | `/admin/invoices/templates…` | Layout templates |

```
┌─ Invoices for order #4821 ───────────────────────────────────────────────┐
│ Invoice number        Rev  Kind      Status      Issued        Actions    │
│ INV-2026-000123        1   Invoice   Generated   4 Oct 06:15   [Download] │
│ INV-2026-000123        2   Invoice   Generated   4 Oct 09:02   [Download] │
│                                        supersedes rev 1         [Download] │
│ CRN-2026-000004        1   Credit    Generated   5 Oct 11:20   [Download] │
│                                                                   [Issue…]│
└──────────────────────────────────────────────────────────────────────────┘
```

```
┌─ INV-2026-000123 rev 1 ──────────────────────────────────────────────────┐
│ ⚠ Rendering failed (attempt 2 of 3)                                      │
│   Renderer returned an empty document.                                    │
│                                                     [ Retry rendering ]  │
└──────────────────────────────────────────────────────────────────────────┘
```

### Override form — wording only, no money fields

```
┌─ Edit invoice wording (rev 1, not yet rendered) ────────────────────────┐
│ Issuer name        [ Kromic Retail Pvt Ltd        ]                     │
│ Bill to            [ Ada Lovelace                ]                     │
│ Notes              [ Refund raised with courier. ]                     │
│ Terms              [ …                            ]                     │
│ Footer note        [ Computer-generated invoice.  ]                     │
│ Accent colour      [ #1a1a1a ]                                          │
│                                                                           │
│ ⓘ Amounts, tax and line items are frozen from the order. This form       │
│   cannot change them.                                                     │
│                                              [ Cancel ]  [ Save ]        │
└───────────────────────────────────────────────────────────────────────────┘
```

`PUT /admin/invoices/{invoiceId}/content` accepts `issuerName`, `billToName`, `notes`, `terms`,
`footerNote`, `accentColor` — and **no amount fields exist in the contract**. A document that has
already been generated is immutable; editing it would make the archived copy disagree with what the
customer holds. An override that changes nothing is rejected.

| Failure | Response | Meaning |
|---|---|---|
| Already generated | `409 ORDER_INVOICE_IMMUTABLE` | Refetch and show it as read-only |
| Nothing changed | `400 ORDER_INVOICE_OVERRIDE_EMPTY` | Disable Save until a field differs |
| Bad accent colour | `400` | Six hex digits, `#` optional |
| Already invoiced, manual issue | `200` | Not an error — render it as "already issued" |

`retry` re-queues **without** resetting the attempt counter, so a permanently failing document
cannot loop forever. Surface `generationAttempts` and `failureReason` on the admin screen — that is
the only place an operator can see why a document is missing.

## 13. Invoice settings

These move to `BusinessSettings.Payment` — **not** the support settings screen.

| Field | Default | Effect |
|---|---|---|
| `invoiceAutoIssueEnabled` | `true` | `false` stops automatic production; manual issue still works |
| `invoiceMailEnabled` | `true` | Email switch. Turning it off stops the **email only** — documents are still generated and downloadable |
| `invoiceMailSubjectOverride` | `null` | Blank restores the default |
| `invoiceNumberPrefix` | `"INV"` | |
| `creditNoteNumberPrefix` | `"CRN"` | |
| `invoicePaymentTermsDays` | `0` | Due date on a manually issued pro-forma |

Render `invoiceMailEnabled` with the copy "**Emails** the invoice to the customer", not "Enables
invoices" — turning it off does not stop invoicing, and a toggle labelled that way will be
switched off by someone who meant to stop the email, and then wonder why nothing is invoiced.

## 14. Error handling

| Code | HTTP | Retryable | Show as |
|---|---|---|---|
| `TICKET_NOT_FOUND` | 404 | No | "This request no longer exists" |
| `TICKET_NOT_OPEN` | 409 | No | Refetch and explain the actual state |
| `TICKET_COMMENT_NOT_FOUND` | 404 | No | "That message is no longer available" |
| `TICKET_COMMENT_INVALID` | 400 | No | Inline — includes the 6-level and 6-attachment ceilings |
| `INVALID_TICKET_STATUS` / `INVALID_TICKET_PRIORITY` | 400 | No | **Your bug**, not the user's |
| `INVALID_MIME_TYPE` / `FILE_TOO_LARGE` | 400 / 413 | No | Inline on the file |
| `UPLOAD_FAILED` | 502 | **Yes** | Retry, or send without the attachment |
| `ORDER_INVOICE_ALREADY_EXISTS` | 200 | — | "Already issued" |
| `ORDER_INVOICE_IMMUTABLE` | 409 | No | Refetch, show read-only |
| `INVOICE_DOCUMENT_NOT_READY` | 409 | **Yes** | "Still preparing" — poll |
| `ORDER_NOT_FOUND` | 404 | No | |

Never show a raw `message` from the error envelope verbatim to a customer. It is written for
developers. Map `code` to your own copy.

---

## 15. Quick reference — every endpoint in this guide

### Customer

| Method | Route | Available |
|---|---|---|
| GET | `/api/v1/tickets?status=&page=&pageSize=` | ✅ now |
| POST | `/api/v1/tickets` | ✅ now |
| GET | `/api/v1/tickets/{ticketId}` | ✅ now |
| POST | `/api/v1/tickets/{ticketId}/comments` | ✅ now |
| POST | `/api/v1/tickets/media` | ✅ now |
| POST | `/api/v1/tickets/{ticketId}/close` | ✅ now |
| POST | `/api/v1/tickets/{ticketId}/reopen` | ✅ now |
| GET | `/api/v1/orders/{id}/invoices` | ❌ later |
| GET | `/api/v1/orders/invoices/{invoiceId}/download` | ❌ later |

### Admin

| Method | Route | Available |
|---|---|---|
| GET | `/api/v1/admin/tickets?status=&priority=&search=&unansweredOnly=` | ✅ now |
| GET | `/api/v1/admin/tickets/{ticketId}` | ✅ now |
| POST | `/api/v1/admin/tickets/{ticketId}/comments?isInternalNote=` | ✅ now |
| POST | `/api/v1/admin/tickets/{ticketId}/resolve` | ✅ now |
| POST | `/api/v1/admin/tickets/{ticketId}/priority` | ✅ now |
| POST | `/api/v1/admin/tickets/{ticketId}/assign` | ✅ now |
| GET/PUT | `/api/v1/admin/support/settings` | ✅ now (no invoice fields) |
| POST | `/api/v1/admin/orders/{id}/invoice/issue` | ❌ later |
| GET | `/api/v1/admin/orders/{id}/invoices` | ❌ later |
| GET/PUT/POST | `/api/v1/admin/invoices/{invoiceId}` · `/content` · `/retry` | ❌ later |
| GET/PUT | `/api/v1/admin/invoices/templates…` | ❌ later |

## 16. Build checklist

**Support desk — safe to build now**

- [ ] Centralised HTTP client: bearer auth, string enums, ISO-8601 UTC, decimal parsing
- [ ] One normalised error type covering both `400` shapes (§ Conventions)
- [ ] Customer: ticket list, with status badge and auto-close countdown
- [ ] Customer: new ticket, with order picker and length limits mirrored
- [ ] Customer: recursive thread, composer, attachment picker capped at `maxAttachmentsPerComment`
- [ ] Customer: close on `Resolved`, reopen on `Resolved`/`Closed`
- [ ] Admin: queue with status / priority / search / unanswered filters
- [ ] Admin: thread with internal notes visually distinct, composer defaulting to internal on a note
- [ ] Admin: resolve, priority, assign. **No** close button
- [ ] Admin: support settings without the three invoice toggles
- [ ] Refetch on window focus
- [ ] **Omit** `latestInvoiceId`, `latestInvoiceStatus`, `ticket.invoices` from your models

**Order invoices — design now, build after the backend lands**

- [ ] Invoice panel on the order detail page, fetched in parallel with the order
- [ ] Handle `Pending` / `Generated` / `Failed` / `current == null`
- [ ] Keep `current` and `superseded` visually distinct
- [ ] Admin invoice list with `generationAttempts` and `failureReason` surfaced
- [ ] Override form with **no** amount inputs, disabled unless the revision is `Pending`
- [ ] Invoice switches in business settings, labelled as email-only where they are email-only