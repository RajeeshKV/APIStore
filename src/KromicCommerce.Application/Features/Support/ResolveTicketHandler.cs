using System.Text.Json;

namespace KromicCommerce.Application.Features.Support;

/// <summary>
/// Admin action: resolve a ticket, and queue its invoice.
///
/// This is the hinge of the whole invoicing feature. Resolution is admin-only precisely
/// because it is what triggers document generation — a customer must not be able to make the
/// system produce an invoice.
///
/// ASYNC BY CONSTRUCTION. Nothing here renders a PDF. It freezes the invoice content into a
/// <see cref="TicketInvoice"/> row in <see cref="TicketInvoiceStatus.Pending"/> and returns;
/// <c>TicketInvoiceWorker</c> picks it up on its next cycle. That keeps the admin's click
/// fast and keeps a renderer outage from turning into a failed support resolution.
///
/// The generated document is itself what triggers the customer email, via a second outbox
/// event. So the chain is: resolve → (same transaction) invoice queued → worker renders →
/// worker enqueues mail → outbox processor sends, subject to the merchant's mailing toggle.
/// </summary>
internal sealed class ResolveTicketHandler(
    IApplicationDbContext db,
    SupportSettingsProvider settingsProvider,
    TicketReferenceGenerator references,
    InvoiceContentComposer composer,
    ILogger<ResolveTicketHandler> logger) : ICommandHandler<ResolveTicketCommand, TicketSummaryResponse>
{
    public async Task<Result<TicketSummaryResponse>> Handle(ResolveTicketCommand cmd, CancellationToken ct)
    {
        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == cmd.TicketId, ct);
        if (ticket is null)
            return Result.Failure<TicketSummaryResponse>(
                Error.NotFound("TICKET_NOT_FOUND", "Ticket not found."));

        if (!ticket.CanResolve)
            return Result.Failure<TicketSummaryResponse>(Error.Conflict(
                "TICKET_NOT_OPEN",
                $"Only an open ticket can be resolved; this one is {ticket.Status}."));

        var customer = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == ticket.CustomerId, ct);

        if (customer is null)
            return Result.Failure<TicketSummaryResponse>(
                Error.NotFound("TICKET_CUSTOMER_NOT_FOUND", "Ticket not found."));

        var settings = await settingsProvider.GetOrCreateAsync(ct);

        // The idle window is stamped onto the ticket at resolution, so changing the merchant
        // setting afterwards cannot move a deadline that is already running.
        // Tracked explicitly: see AddTicketCommentHandler for why a new child of an
        // already-persisted aggregate is not inserted by change tracking alone.
        db.TicketStatusHistory.Add(
            ticket.Resolve(cmd.AdminId, cmd.ResolutionNote, settings.AutoCloseIdleHours));

        if (settings.NotifyCustomerOnTicketResolved)
        {
            // Resolved server-side rather than accepted from the caller: the audit log and the
            // email signature must record the account that actually acted, and the handler is
            // the only place that can be sure the id was not tampered with upstream.
            var adminName = await db.Users.AsNoTracking()
                .Where(u => u.Id == cmd.AdminId)
                .Select(u => u.FullName)
                .FirstOrDefaultAsync(ct) ?? "Support Team";

            db.OutboxEvents.Add(OutboxEvent.Create(TicketOutbox.Resolved, JsonSerializer.Serialize(
                new TicketResolvedPayload(
                    ticket.Id,
                    cmd.AdminId,
                    adminName,
                    customer.FullName,
                    customer.Email,
                    ticket.TicketNumber,
                    ticket.Subject,
                    cmd.ResolutionNote,
                    DateTime.UtcNow))));
        }

        // The admin may force or suppress the invoice for this one ticket. Null honours the
        // merchant's standing preference.
        var wantsInvoice = cmd.RequestInvoice ?? settings.AutoGenerateInvoiceOnResolve;

        if (wantsInvoice)
        {
            var queued = await QueueInvoiceAsync(ticket, customer, cmd, ct);

            if (queued.IsFailure)
            {
                // Not fatal. A missing order or an unusable template must not block the
                // resolution itself — the conversation is what matters, the document can be
                // queued again from the ticket screen.
                logger.LogWarning(
                    "Ticket {TicketNumber} was resolved without an invoice: {Code}",
                    ticket.TicketNumber, queued.Error.Code);
            }
        }

        var summary = await TicketReadModel.LoadSummaryAsync(db, ticket, ct);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Ticket {TicketNumber} resolved by {AdminId} (invoice: {Invoice})",
            ticket.TicketNumber, cmd.AdminId, wantsInvoice ? "queued" : "skipped");

        return Result.Success(summary);
    }

    private async Task<Result> QueueInvoiceAsync(
        Ticket ticket,
        User customer,
        ResolveTicketCommand cmd,
        CancellationToken ct)
    {
        // No order means nothing to bill. "Where is my parcel" is a legitimate ticket and must
        // not produce a zero-value invoice.
        Order? order = null;
        if (ticket.RelatedOrderId is { } orderId && orderId != Guid.Empty)
        {
            order = await db.Orders
                .Include(o => o.Items)
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == orderId && o.CustomerId == ticket.CustomerId, ct);
        }

        if (order is null)
            return Result.Failure(Error.Validation(
                "TICKET_NO_ORDER_CONTEXT",
                "This ticket is not linked to an order, so there is nothing to invoice."));

        var template = await ResolveTemplateAsync(cmd.InvoiceTemplateId, ct);

        var content = await composer.ComposeAsync(ticket, order, customer, template, ct);
        if (content is null)
            return Result.Failure(Error.Validation(
                "TICKET_INVOICE_CONTENT_INVALID",
                "Invoice content could not be composed from the order."));

        var existing = await db.TicketInvoices
            .AsNoTracking()
            .Where(i => i.TicketId == ticket.Id)
            .OrderBy(i => i.Revision)
            .ToListAsync(ct);

        // Do not queue a second document for a resolution that already produced one. A
        // double resolve is a conflict upstream, but the guard here means a retry cannot
        // mail the customer two invoices.
        if (existing.Any(i => i.Status is TicketInvoiceStatus.Pending or TicketInvoiceStatus.Generated))
            return Result.Failure(Error.Conflict(
                "TICKET_INVOICE_ALREADY_EXISTS",
                "This ticket already has an invoice that has not been superseded."));

        var revision = existing.Count == 0 ? 1 : existing.Max(i => i.Revision) + 1;

        // A revision that replaces a generated document supersedes it. The old row is kept:
        // it may already have been mailed and filed.
        foreach (var superseded in existing.Where(i => i.Status == TicketInvoiceStatus.Generated))
            superseded.MarkSuperseded();

        var invoiceNumber = $"{await references.NextInvoiceNumberAsync(ct)}-R{revision}";
        var invoice = TicketInvoice.Create(
            ticket.Id, invoiceNumber, revision, content, template?.Id, cmd.AdminId);

        db.TicketInvoices.Add(invoice);
        ticket.SetLatestInvoice(invoice.Id);

        // No outbox event is written here on purpose. The render worker claims work by
        // polling for Pending rows rather than by subscribing to an event, which means a
        // crash between commit and pickup costs nothing — the row is still there.

        logger.LogInformation(
            "Invoice {InvoiceNumber} queued for ticket {TicketNumber} (revision {Revision}, template: {TemplateId})",
            invoiceNumber, ticket.TicketNumber, revision, template?.Id.ToString() ?? "built-in default");

        return Result.Success();
    }

    private async Task<InvoiceTemplate?> ResolveTemplateAsync(Guid? templateId, CancellationToken ct)
    {
        if (templateId is { } requested && requested != Guid.Empty)
        {
            var template = await db.InvoiceTemplates
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == requested, ct);

            if (template is not null) return template;
        }

        // No explicit selection, or the id no longer resolves: fall back to the seeded default.
        return await db.InvoiceTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.IsDefault, ct);
    }
}