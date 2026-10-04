# 39 — Customer Support Desk and Order Invoice Generation

> **Scope change (2026-10-04).** Invoices were originally specified as an artefact of *ticket
> resolution*. They are now specified as an artefact of the *order*. Every ticket-invoice rule
> below has been removed and replaced. Section A (support desk) is unchanged except where it
> previously triggered invoicing. Section B is the new specification.
>
> **Implementation status: Section B is not yet implemented.** The code in this repository still
> builds invoices from ticket resolution (`TicketInvoice`, `InvoiceContentComposer`,
> `TicketInvoiceWorker`, `ResolveTicketHandler.QueueInvoiceAsync`). Section B.10 lists the concrete
> work required. The API reference in `API-Reference.md` has been updated to match this document.

---

# Part A — Support Desk

## A.1 What this delivers

A support conversation from open to close, with threaded replies, media, a status audit trail,
and time-based closure. The desk **references** orders but no longer produces financial
documents — see Section B.

## A.2 Data model

| Table | Purpose |
|-------|---------|
| `tickets` | One conversation. Owns status, priority, assignee, activity clocks. |
| `ticket_comments` | Threaded replies with a materialised `ThreadPath`. |
| `ticket_attachments` | Image/video attached to a comment. |
| `ticket_status_history` | Append-only audit log of every transition. |
| `support_settings` | Singleton support-desk automation switches. |

`tickets.RelatedOrderId` links a conversation to the order it is about, with `ON DELETE SET NULL`
so removing an order does not erase the record of what the customer was told. That link is now
also what lets an administrator raise a credit note against the order (B.4).

`support_settings` no longer carries any invoice field. See B.7.

## A.3 Reference numbering

Ticket references come from a **PostgreSQL sequence** (`ticket_reference_seq`), not a row count. A
count-based allocator reads then writes, so two concurrent ticket creates both observe count `N`
and both try to insert `N+1` — one loses on the unique index and the customer sees a 500.

This sequence is **now dedicated to tickets**. It previously served invoices as well; invoice
numbering moved to `invoice_reference_seq` (B.3), because the two document series are now
independent and must each be monotonic on their own.

## A.4 Threading without recursion

`ticket_comments.ThreadPath` is a materialised path (`0000000000000000001`,
`0000000000000000001/0000000000000000003`, …). Ordering by it returns the entire conversation in
reading order from **one indexed scan** — no recursion, no self-join, no query per nesting level.
The unique index on `(TicketId, ThreadPath)` is the backstop against a lost parent-count race.

Attachments are fetched as a second query keyed by comment id rather than an `Include`. An
Include of a collection makes EF emit one row per comment per attachment, so 200 comments with one
image each returns 400 rows to reassemble 200 comments.

## A.5 Ownership of lifecycle transitions

The asymmetry is deliberate and enforced in the domain, not only in validators:

| Transition | Actor | Why |
|-----------|-------|-----|
| Create, reopen | Customer | Asking for help is the customer's action. |
| **Resolve** | **Administrator** | Resolution is a merchant commitment that the issue is handled. It is no longer a document trigger — that moved to the order lifecycle (B.4). |
| Close | Customer (confirm) or idle worker | Confirmation that the fix worked. |
| Comment | Both | |

`Ticket.Close` throws if the actor is an administrator — closure is the customer's confirmation,
or the worker acting on their behalf. Letting an admin close it would bypass the confirmation.

A customer replying to a **closed** ticket reopens it rather than posting onto a closed thread
nobody is watching. A comment that silently did nothing would lose the message.

## A.6 Time-based closure

`AutoCloseAtUtc` is persisted rather than computed at read time from `LastActivityAtUtc`, so the
worker's query is a plain indexed range scan over a partial index:

```sql
WHERE "AutoCloseAtUtc" IS NOT NULL AND "Status" = 'Resolved'
```

Two rules matter:

- The deadline is re-stamped from **customer** activity only. An admin note 80 hours after
  resolution does not keep an abandoned conversation open — that is not what the deadline is for.
- The idle window in hours is stamped onto the ticket at resolution, so changing the merchant
  setting afterwards cannot move a deadline that is already running.

## A.7 Multi-instance safety

Several API instances run the worker loop. All may select the same row. The first to commit wins;
the others throw from `Ticket.Close` because the status is no longer `Resolved`, and that is
swallowed per row as an expected outcome. An in-memory lock would not help across processes; a
compare-and-set `UPDATE` would, and is the natural upgrade if duplicate-notification volume ever
becomes a problem.

## A.8 Security boundaries

- **No request field names an actor.** Every `customerId` / `adminId` comes from
  `ICurrentUserService`. `IsAdmin` is derived from the role claim, never from the body.
- **Ownership is re-checked in the handler,** not only in the controller. A guessed ticket id
  returns `404`, never `403` — a 403 confirms the id exists.
- **Internal notes are filtered by the mapper.** A customer's request never receives one,
  regardless of what the UI does with it. Replying underneath one is refused with `404` for the
  same reason: answering would reveal that it exists.
- **Uploads use an allow-list** of MIME types, not a deny-list, and are partitioned per customer
  folder so one account's uploads cannot be enumerated through the media console.

## A.9 EF Core tracking gotcha

`Entity.Id` assigns a client-side `Guid`, but EF still treats the key as store-generated. A new
child appended to an **already-persisted** aggregate through its navigation is therefore
discovered as an *existing* row and marked `Modified`; saving raises
`DbUpdateConcurrencyException` ("expected to affect 1 row(s)"), because no such row exists.

Building a whole aggregate and adding the root works, because `Add()` on the root propagates.
Adding a child to a tracked aggregate does not. Every such site must track the row explicitly:

```csharp
db.TicketStatusHistory.Add(ticket.Resolve(adminId, note, idleHours));
db.TicketComments.Add(comment);
```

`Resolve`, `Close` and `Reopen` return their appended `TicketStatusHistory` precisely so callers
cannot forget this.

---

# Part B — Order Invoice Generation

## B.1 Purpose and scope

Every order produces a PDF **tax invoice** that the customer can download from their order detail
page and, unless mailing is disabled, receive by email.

**In scope:** issue trigger, numbering, frozen content, revisions, credit notes, PDF rendering,
mailing, templates, download authorisation.

**Out of scope:** tax computation changes, payment capture, refunds themselves, GST/e-invoicing
portal filing, multi-currency conversion. Amounts are read from the existing order snapshot; this
feature never recomputes them.

## B.2 Domain model

| Table | Purpose |
|-------|---------|
| `order_invoices` | One row per issued document (invoice or credit note) for an order. |
| `invoice_templates` | Layout presets. Exactly one is the default. Unchanged from the original design. |

`order_invoices` replaces `ticket_invoices`. The rename is the single largest code change and is
detailed in B.10.

### B.2.1 `OrderInvoice`

| Field | Type | Rule |
|-------|------|------|
| `Id` | `uuid` | Client-assigned, per `Entity`. |
| `OrderId` | `uuid` | FK → `orders.Id`, `ON DELETE CASCADE`. |
| `InvoiceNumber` | `varchar(64)` | Unique. Quoted on the document. See B.3. |
| `Kind` | `varchar(16)` | `Invoice` or `CreditNote`. |
| `Revision` | `int` | 1-based, `>= 1`. Unique per order. See B.4. |
| `Status` | `varchar(24)` | `Pending` → `Generated` \| `Failed` \| `Superseded`. |
| `TemplateId` | `uuid?` | FK → `invoice_templates.Id`, `ON DELETE SET NULL`. |
| `Content` | owned columns | Frozen snapshot. See B.5. |
| `QueuedAtUtc` | `timestamptz` | When the document was requested. |
| `GeneratedAtUtc` | `timestamptz?` | When the PDF succeeded. |
| `GenerationAttempts` | `int` | `0..3`, CHECK-constrained. |
| `PdfContent` | `bytea?` | Rendered bytes. |
| `FileName` | `varchar(200)` | e.g. `INV-2026-000123.pdf`. |
| `SizeBytes` | `bigint` | |
| `Checksum` | `varchar(64)?` | SHA-256, lowercase hex. Detects silent corruption of the stored bytes. |
| `FailureReason` | `varchar(2000)?` | Populated on `Failed`. |
| `IsContentOverridden` | `bool` | An administrator edited wording after queue. |
| `EmailedToCustomer` | `bool` | Send idempotency guard. |
| `EmailedAtUtc` | `timestamptz?` | |
| `CancellationReason` | `varchar(2000)?` | Credit notes only, mirrors `orders.CancellationReason`. |

Indexes:

- `ux_order_invoices_order_revision` UNIQUE `(OrderId, Revision)` — one revision number per order.
- `ux_order_invoices_invoice_number` UNIQUE `(InvoiceNumber)`.
- `ix_order_invoices_status_queued` `(Status, QueuedAtUtc)` — the render worker's claim query.
- `ix_order_invoices_template` `(TemplateId)`.

`orders.LatestInvoiceId` (`uuid?`) is added, pointing at the newest revision. `tickets.LatestInvoiceId`
is **removed**.

### B.2.2 `InvoiceContent` (owned, flattened onto `order_invoices`)

Every field is captured **once**, at issue time, and never recomputed. This is the single most
important rule in Part B: an issued invoice is a historical document and must not change when the
catalogue, prices, tax settings, or store identity change afterwards.

**Issuer** — from `BusinessSettings`, with template overrides applied:

| Field | Source |
|-------|--------|
| `IssuerName` | `InvoiceTemplate.IssuerNameOverride` ?? `BusinessSettings.LegalName` ?? `BusinessSettings.BusinessName` ?? `"Store"` |
| `IssuerAddress` | `BusinessSettings.Address` |
| `IssuerEmail` | `BusinessSettings.SupportEmail` |
| `IssuerTaxId` | `InvoiceTemplate.TaxId` |
| `IssuerLogoUrl` | `BusinessSettings.LogoUrl` |
| `IssuerWebsiteUrl` | `BusinessSettings.WebsiteUrl` |

**Bill to** — the order's frozen `ShippingAddress` plus the customer account:

| Field | Source |
|-------|--------|
| `BillToName` | `ShippingAddress.FullName` |
| `BillToEmail` | `User.Email` |
| `BillToPhone` | `ShippingAddress.Phone` |
| `BillToAddress` | `AddressLine1`, `AddressLine2`, `City`, `State`, `PostalCode`, `Country`, joined |

**Document** — `InvoiceDateUtc`, `DueDateUtc?`, `OrderNumber`, `OrderPlacedAtUtc`, `CurrencyCode`,
`PaymentMethod`, `PaymentReference?`, `AmountPaidAtUtc?`.

**Tax presentation** — `TaxLabel`, `TaxPercentage`, `IsPriceInclusive`, snapshotted from
`BusinessSettings.Tax`. These drive how the renderer *labels* the tax line; the tax **amount** is
`Order.TaxAmount` and is never recomputed. `IsPriceInclusive` changes the label from
"added on top" to "included in price", which is why it must be frozen.

**Figures** — `Subtotal`, `DiscountAmount`, `TaxAmount`, `ShippingAmount`, `CodFee`, `GrandTotal`,
`RefundedAmount`, all copied from `Order`. Read-only thereafter.

**Lines** — `LineItemsJson`: one entry per `OrderItem`, in `Order.Items` order, carrying
`Description` (`ProductName` + `VariantDescription`), `Sku`, `Quantity`, `UnitPrice`, `LineTotal`,
and `HsnCode?`. All of these are already immutable order snapshots, so the invoice inherits their
historical accuracy. `OrderItem.UnitPrice * OrderItem.Quantity` is used as-is; it is never
recalculated from the current catalogue.

**Presentation wording** — `Notes?`, `Terms?`, `FooterNote?`, `AccentColor` from the template.

## B.3 Numbering

Two dedicated sequences, created by the migration, not lazily — both allocators issue a bare
`nextval()` with no existence check:

| Sequence | Documents | Format | Example |
|----------|-----------|--------|---------|
| `invoice_reference_seq` | Tax invoices | `{prefix}-{year}-{seq:000000}` | `INV-2026-000123` |
| `invoice_reference_seq` | Credit notes | `{creditPrefix}-{year}-{seq:000000}` | `CRN-2026-000004` |

Revisions append a suffix: `INV-2026-000123-R2`. The suffix is part of `InvoiceNumber`, so
uniqueness holds without a compound key.

- **Prefixes are merchant-editable** (`InvoiceNumberPrefix`, default `INV`;
  `CreditNoteNumberPrefix`, default `CRN`) and live in `BusinessSettings.Payment`. Unlike
  `Support__AdminNotificationEmail` there is no security reason to freeze them, so they are an
  ordinary store preference.
- **Year is UTC**, so a document issued at 00:30 UTC on 1 January is unambiguous about its cycle.
- A single sequence serves both kinds. Two would allocate disjoint ranges a reader could not tell
  apart; the differing prefixes disambiguate them.
- The sequence is **not** reset per year. `START WITH 1`, monotonic for the life of the database.

## B.4 Issue trigger

**R-INV-1.** An invoice is queued automatically when the order's money becomes committed:

| Payment method | Condition | Source event |
|----------------|-----------|--------------|
| Razorpay (prepaid) | `Payment.Status == Paid` **and** `Order.PaidAt != null` | `PaymentSucceededEvent` |
| CashOnDelivery | `Order.Status == Delivered` | `OrderStatusChangedEvent(Delivered)` |

**R-INV-2.** COD invoices are issued at `Delivered`, not at `Confirmed`. At `Delivered` the amount
is actually payable, so the document never enters an "issued but unpaid" state that would later
need mutating. A merchant that requires a delivery challan before dispatch uses manual issue
(R-INV-5), which produces a pro-forma document with a due date.

**R-INV-3.** `InvoiceAutoIssueEnabled = false` suppresses automatic queueing entirely. Orders
still complete; only automatic document production stops. Manual issue still works.

**R-INV-4 — idempotency.** A trigger event that arrives twice, or two handlers racing, must
produce exactly one revision 1. The handler checks for an existing non-`Superseded` invoice for
the order and returns it unchanged; `ux_order_invoices_order_revision` is the backstop. Order
status changes are not idempotent in general — `Confirm()` throws on a repeat — so the guard
cannot be skipped.

**R-INV-5 — manual issue.** `POST /api/v1/admin/orders/{id}/invoice/issue` (B.8) queues a document
for an order that has none, or a new revision for an order that does. Use cases: pro-forma /
delivery challan, re-issue after a renderer outage, correcting a document that was never mailed.
A manually issued prepaid document carries `DueDateUtc = now + InvoicePaymentTermsDays` and
`AmountPaidAtUtc = null`, so it renders as *payable* rather than *paid*.

**R-INV-6.** Cancelled and refunded orders are never silently re-issued. They are handled by
credit notes (R-INV-8), which are separate documents.

## B.5 Revisions and credit notes

**R-REV-1.** Revision numbers are 1-based and contiguous per order. A new revision is created
**only** for one of these reasons:

| Trigger | New revision `Kind` |
|---------|----------------------|
| Admin manual issue of an already-invoiced order | `Invoice` |
| Order cancelled | `CreditNote` |
| Refund initiated or settled | `Invoice` (carries `RefundedAmount`) |
| Admin content override that changes figures | *refused* — see R-REV-4 |

**R-REV-2.** Anything else — a comment, a status change, a priority edit, an admin note — must not
create a revision. A revision is a new financial document, not a new version of a rendering.

**R-REV-3.** When a revision is created, the previous revision's status becomes `Superseded`. The
row is **retained**, never deleted or mutated: it may already have been mailed and filed. Only the
highest non-`Superseded` revision is the current document, and only that one is returned as
"the" invoice in list responses; the rest are returned in a separate `superseded` array.

**R-REV-4.** An admin may override presentation wording on a `Pending` revision only
(`IssuerName`, `BillToName`, `Notes`, `Terms`, `FooterNote`, `AccentColor`). Once a document is
`Generated` it is immutable — editing it would make the archived document disagree with what the
customer holds. The override DTO **contains no amount fields at all**, so a presentation edit
cannot alter a figure.

**R-REV-5.** An override that changes nothing is rejected (`400`), because it would flag the
revision as customised and record a misleading audit trail.

**R-REV-6 — credit notes.** A cancelled order produces a `CreditNote` quantifying the reversal:

- `GrandTotal` on the credit note is the amount being reversed, not zero.
- `RefundReference` / `RefundedAtUtc` are captured from the payment when a refund is involved;
  a cancellation without a captured payment (COD) has no refund reference.
- A credit note is emailed only if it references a captured refund, since emailing a reversal for
  money that never arrived is confusing. Otherwise it is available for download only.

**R-REV-7.** An order may have at most one open (non-`Superseded`) credit note per revision
trigger. A duplicate cancellation attempt does not produce a second credit note.

## B.6 Rendering

**R-REN-1.** Rendering is asynchronous and happens in `OrderInvoiceWorker`, which claims
`Pending` rows by **polling**, not by subscribing to an event. A crash between commit and pickup
costs nothing because the row is still there. This is unchanged from the original design.

**R-REN-2.** Failure is data, not an exception. The worker records `FailureReason`, increments
`GenerationAttempts`, and moves on. After `MaxGenerationAttempts` (3) the row stays `Failed` and
is surfaced on the admin order screen with its reason.

**R-REN-3.** A successful render stores `PdfContent`, `SizeBytes`, and a SHA-256 `Checksum`, and
sets `GeneratedAtUtc`. The checksum lets a later download detect byte-level corruption of the
stored document.

**R-REN-4.** The renderer takes a frozen `InvoiceContent` plus the template's presentation flags.
It must not query the database for figures, prices, or store identity — if it did, a template
change would retroactively alter an issued document.

**R-REN-5.** A partially-rendered or empty PDF is never marked `Generated`. Verify the byte stream
starts with the `%PDF-` magic number before persisting; a zero-length or truncated file that
reaches a customer is worse than a visible failure.

**R-REN-6.** `ITicketInvoiceRenderer` is renamed `IInvoiceRenderer` and moved from
`Abstractions/Support` to `Abstractions/Invoicing`. `InvoiceRenderRequest` drops `TicketNumber` and
gains `OrderNumber`, `PaymentMethod`, `PaymentReference`, `AmountPaidAtUtc`, `DueDateUtc`,
`TaxLabel`, `TaxPercentage`, `IsPriceInclusive`, `AppliedCouponCode`, `IssuerLogoUrl`, `Hsncodes`.

## B.7 Configuration

### B.7.1 Runtime — `BusinessSettings.Payment` (owned)

| Field | Default | Rule |
|-------|---------|------|
| `InvoiceAutoIssueEnabled` | `true` | R-INV-3. |
| `InvoiceMailEnabled` | `true` | Master switch for B.9 mailing. Turning off stops the email only; the document is still generated and downloadable. |
| `InvoiceMailSubjectOverride` | `null` | Blank restores the default subject. Max 200 chars. |
| `InvoiceNumberPrefix` | `"INV"` | Letters, digits, hyphen. Max 10 chars. |
| `CreditNoteNumberPrefix` | `"CRN"` | Same. |
| `InvoicePaymentTermsDays` | `0` | Used by manual pro-forma issue (R-INV-5). Range 0–365. |

These live in `BusinessSettings` rather than `SupportSettings` because they are order and
commerce settings, not support-desk settings, and because `BusinessSettings` is already the
merchant-editable store configuration surface with its own cache-invalidation path.

### B.7.2 Removed from `SupportSettings`

`AutoGenerateInvoiceOnResolve`, `AutomatedInvoiceMailingEnabled`, and `InvoiceMailSubjectOverride`
are **deleted**. The first has no meaning once invoicing is not tied to resolution; the other two
move to `BusinessSettings.Payment`. `SupportSettings` retains only `AutoCloseIdleHours`,
`NotifyAdminOnTicketCreated`, `NotifyAdminOnTicketReopened`,
`NotifyCustomerOnTicketResolved`, and `MaxAttachmentsPerComment`.

### B.7.3 Deployment-level environment variables

The `Support__InvoiceNumberPrefix` key is removed; numbering prefixes are now merchant settings
(B.7.1). `Support__AdminNotificationEmail` remains the only deployment-level support variable,
because the address is frozen for the open-relay reason in A.8.

## B.8 API surface

All routes are versioned `api/v{version:apiVersion}`. Removed routes are listed in B.10.

### B.8.1 Customer — `/api/v1/orders`

| Method | Route | Result |
|--------|-------|--------|
| GET | `/orders/{id}/invoices` | `200` `OrderInvoiceListResponse` — current plus superseded revisions |
| GET | `/orders/invoices/{invoiceId}/download` | `200` PDF |

Authorisation: the order must belong to the caller, else `404` (never `403`). The existing
`GET /orders/{id}` is already customer-scoped; the invoice routes must apply the same check.

### B.8.2 Admin — `/api/v1/admin`

| Method | Route | Result |
|--------|-------|--------|
| POST | `/orders/{id}/invoice/issue` | `201` queued, `200` already invoiced (R-INV-5) |
| GET | `/orders/{id}/invoices` | `200` `OrderInvoiceListResponse` |
| GET | `/invoices/{invoiceId}` | `200` `OrderInvoiceResponse` |
| GET | `/invoices/{invoiceId}/download` | `200` PDF |
| PUT | `/invoices/{invoiceId}/content` | `200` — `Pending` only (R-REV-4) |
| POST | `/invoices/{invoiceId}/retry` | `200` — re-enqueue a `Failed` revision |
| GET | `/invoices/templates` | `200` `InvoiceTemplateResponse[]` |
| GET | `/invoices/templates/{templateId}` | `200` |
| PUT | `/invoices/templates/{templateId}` | `200` |
| POST | `/orders/{id}/cancel` → credit note | automatic on cancellation |

`retry` re-enqueues without resetting `GenerationAttempts` to zero, so a revision that has
exhausted its attempts cannot loop forever.

### B.8.3 Contracts

`OrderInvoiceResponse` mirrors the old `TicketInvoiceResponse` with these deltas: `TicketId` →
`OrderId`; `OrderNumber` added and non-null; `Kind` added; `InvoiceContentResponse` gains
`BillToPhone`, `OrderPlacedAtUtc`, `PaymentMethod`, `PaymentReference`, `AmountPaidAtUtc`,
`DueDateUtc`, `TaxLabel`, `TaxPercentage`, `IsPriceInclusive`, `AppliedCouponCode`,
`IssuerLogoUrl`; `TicketNumber` / `TicketSubject` removed.

`OrderInvoiceListResponse` is `{ current: OrderInvoiceResponse?, superseded: OrderInvoiceResponse[] }`
per R-REV-3.

## B.9 Mailing

**R-MAIL-1.** The document is emailed as a PDF attachment by `OutboxProcessor`, driven by an
outbox event written by the render worker. A mail failure never rolls back document generation.

**R-MAIL-2.** The subject is `InvoiceMailSubjectOverride` when set, otherwise
`Invoice {InvoiceNumber} for order {OrderNumber}`.

**R-MAIL-3 — idempotency.** The outbox can retry a send whose acknowledgement was lost. Without a
guard, a transient provider timeout produces a second invoice email, which to a customer reads as
a billing system that cannot count. `EmailedToCustomer` / `EmailedAtUtc` are written **before** the
outbox row is marked processed, so losing that race resends rather than losing the send.

**R-MAIL-4.** `InvoiceMailEnabled = false` stops the email only. The document remains generated
and downloadable, and the suppression is logged with the invoice number so the gap is visible.

**R-MAIL-5.** `InvoiceMailContext` drops the `TicketNumber` field and its `OrderNumber` becomes
required. `TicketNumber` has no meaning for an order document and leaving it would put a dead
field in every mail template.

**R-MAIL-6.** Credit notes are emailed only when they reference a captured refund (R-REV-6).

## B.10 Implementation checklist

**Removed (ticket-invoice logic):**

1. `Domain/Support/TicketInvoice.cs`, `Domain/Support/InvoiceContent.cs`, `TicketInvoiceStatus.cs`
   → replaced by `Domain/Orders/OrderInvoice.cs`, `Domain/Orders/InvoiceContent.cs`,
   `OrderInvoiceKind.cs`, `OrderInvoiceStatus.cs`.
2. `Tickets.LatestInvoiceId`; add `Orders.LatestInvoiceId`.
3. `SupportSettings`: delete `AutoGenerateInvoiceOnResolve`,
   `AutomatedInvoiceMailingEnabled`, `InvoiceMailSubjectOverride`.
4. `ResolveTicketCommand.InvoiceTemplateId` and `ResolveTicketRequest.RequestInvoice`; the whole
   `QueueInvoiceAsync` path in `ResolveTicketHandler`.
5. `Infrastructure/BackgroundServices/TicketInvoiceWorker.cs` → `OrderInvoiceWorker.cs`.
6. `Abstractions/Support/ITicketInvoiceRenderer.cs` → `Abstractions/Invoicing/IInvoiceRenderer.cs`.
7. Outbox event `TicketInvoiceGenerated` and its payload; add `OrderInvoiceGenerated`.
8. `SupportOptions.InvoiceNumberPrefix`.
9. Ticket-scoped invoice endpoints and `AdminSupportController` invoice routes.

**Added:**

10. `OrderInvoiceCreatedHandler` (prepaid trigger), `OrderDeliveredInvoiceHandler` (COD trigger),
    `IssueOrderInvoiceCommandHandler` (manual), `RetryOrderInvoiceHandler`.
11. `BusinessSettings.Payment` invoice fields + migration columns.
12. `invoice_reference_seq`.
13. `OrderInvoiceWorker` claim + render + checksum + outbox write.
14. `OrderInvoicesController` (customer) and invoice routes on `AdminOrdersController`, or a
    dedicated `AdminInvoicesController`.
15. `InvoiceContentComposer` re-pointed from `Ticket` to `Order`.
16. `InvoiceMailContext.TicketNumber` removed.

**Tests to add:** trigger-per-payment-method; idempotent re-delivery of a trigger event;
revision supersession; credit note on cancellation; content override refused once generated;
download authorisation returns 404 for a foreign order; checksum detects corrupted bytes;
mail idempotency under a simulated retry.

**Migration note.** The `SupportDesk` migration is uncommitted and has not been deployed, so the
correct path is to **edit that migration in place** rather than add a corrective one — leaving dead
`ticket_invoices` columns in a fresh install serves no purpose. If it has already been applied to
any environment, add a follow-up migration that renames the table and drops the moved columns;
`UPDATE support_settings` must set the new `BusinessSettings.Payment` values first, since
`InvoiceMailEnabled` defaults to `true` and silently re-enabling mailing on a deployment that had
it off is the risk to manage.

## B.11 Tests that must exist for this feature

Integration, against real PostgreSQL — the guarantees are database guarantees, and a sequence
allocator, unique revision index, partial unique default-template index, and CHECK constraints are
all invisible to an in-memory provider. Each test names the failure it prevents.

1. Concurrent triggers for one order produce exactly one revision 1.
2. `ux_order_invoices_order_revision` rejects a duplicate revision at the database level.
3. A cancelled order produces exactly one credit note.
4. A superseded revision is retained and still downloadable.
5. A customer cannot download another customer's invoice (`404`).
6. The credit note grand total equals the reversed amount, not zero.
7. `InvoiceMailEnabled = false` leaves the document downloadable but unsent.
8. Content override is accepted on `Pending` and refused on `Generated`.