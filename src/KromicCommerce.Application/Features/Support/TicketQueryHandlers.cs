namespace KromicCommerce.Application.Features.Support;

internal sealed class GetMyTicketsQueryHandler(IApplicationDbContext db)
    : IQueryHandler<GetMyTicketsQuery, PagedResponse<TicketSummaryResponse>>
{
    private const int MaxPageSize = 50;

    public async Task<Result<PagedResponse<TicketSummaryResponse>>> Handle(GetMyTicketsQuery query, CancellationToken ct)
    {
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var page = Math.Max(1, query.Page);

        // The owner filter is applied here, from the token-derived CustomerId. There is no
        // request field that could widen it.
        var filtered = db.Tickets.AsNoTracking()
            .Where(t => t.CustomerId == query.CustomerId);

        if (query.Status is { } status)
            filtered = filtered.Where(t => t.Status == status);

        var (rows, total) = await TicketReadModel.LoadPageAsync(filtered, db, page, pageSize, ct);

        return Result.Success(new PagedResponse<TicketSummaryResponse>(
            rows.Select(TicketMapper.MapSummary).ToArray(), page, pageSize, total));
    }
}

internal sealed class GetAdminTicketsQueryHandler(IApplicationDbContext db)
    : IQueryHandler<GetAdminTicketsQuery, PagedResponse<TicketSummaryResponse>>
{
    private const int MaxPageSize = 100;

    public async Task<Result<PagedResponse<TicketSummaryResponse>>> Handle(GetAdminTicketsQuery query, CancellationToken ct)
    {
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var page = Math.Max(1, query.Page);

        // Filtered and ordered against the ticket table itself; the enriched projection is
        // fetched afterwards for the ids that survive paging. See TicketReadModel.LoadPageAsync.
        var filtered = db.Tickets.AsNoTracking();

        if (query.Status is { } status)
            filtered = filtered.Where(t => t.Status == status);

        if (query.Priority is { } priority)
            filtered = filtered.Where(t => t.Priority == priority);

        // Open and never answered by an administrator.
        if (query.UnansweredOnly == true)
            filtered = filtered.Where(t => t.Status == TicketStatus.Open && t.FirstResponseAtUtc == null);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Provider-neutral match, consistent with the review admin queue. The term is
            // passed as a parameter, never concatenated, so it cannot alter the statement.
            //
            // Customer and order fields are matched with EXISTS subqueries rather than by
            // joining those tables into the projection: joining leaves EF unable to reduce the
            // ORDER BY back to a column, which fails translation.
            //
            // The name is matched as FirstName + " " + LastName in SQL because User.FullName is
            // a computed property with no mapped column, so EF cannot translate it inside a
            // predicate. COALESCE keeps a half-filled name searchable.
            var term = query.Search.Trim().ToLower();
            filtered = filtered.Where(t =>
                t.TicketNumber.ToLower().Contains(term) ||
                t.Subject.ToLower().Contains(term) ||
                db.Users.Any(u => u.Id == t.CustomerId &&
                    ((u.FirstName ?? "") + " " + (u.LastName ?? "")).ToLower().Contains(term) ||
                    u.Email.ToLower().Contains(term)) ||
                (t.RelatedOrderId != null &&
                 db.Orders.Any(o => o.Id == t.RelatedOrderId && o.OrderNumber.ToLower().Contains(term))));
        }

        var (rows, total) = await TicketReadModel.LoadPageAsync(filtered, db, page, pageSize, ct);

        return Result.Success(new PagedResponse<TicketSummaryResponse>(
            rows.Select(TicketMapper.MapSummary).ToArray(), page, pageSize, total));
    }
}

internal sealed class GetTicketQueryHandler(IApplicationDbContext db)
    : IQueryHandler<GetTicketQuery, AdminTicketDetailResponse>
{
    public async Task<Result<AdminTicketDetailResponse>> Handle(GetTicketQuery query, CancellationToken ct)
    {
        var ticket = await db.Tickets
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == query.TicketId, ct);

        // A non-owner gets NotFound rather than Forbidden, so the endpoint does not confirm
        // that a given ticket id exists.
        if (ticket is null || (!query.IsAdmin && !ticket.IsOwnedBy(query.ActorId!.Value)))
            return Result.Failure<AdminTicketDetailResponse>(
                Error.NotFound("TICKET_NOT_FOUND", "Ticket not found."));

        var customer = await db.Users.AsNoTracking()
            .Where(u => u.Id == ticket.CustomerId)
            .Select(u => new { u.FullName, u.Email })
            .FirstAsync(ct);

        var orderNumber = ticket.RelatedOrderId is { } orderId
            ? await db.Orders.AsNoTracking()
                .Where(o => o.Id == orderId)
                .Select(o => o.OrderNumber)
                .FirstOrDefaultAsync(ct)
            : null;

        var nodes = await TicketCommentLoader.LoadThreadAsync(db, ticket.Id, ct);

        var invoices = await db.TicketInvoices
            .AsNoTracking()
            .Where(i => i.TicketId == ticket.Id)
            .OrderByDescending(i => i.Revision)
            .ToListAsync(ct);

        var history = await db.TicketStatusHistory
            .AsNoTracking()
            .Where(h => h.TicketId == ticket.Id)
            .OrderBy(h => h.OccurredAtUtc)
            .ToListAsync(ct);

        var detail = new TicketDetailResponse(
            ticket.Id,
            ticket.TicketNumber,
            ticket.Subject,
            ticket.Description,
            ticket.Status.ToString(),
            ticket.Priority.ToString(),
            ticket.CustomerId,
            customer.FullName,
            customer.Email,
            ticket.AssignedAdminId,
            ticket.RelatedOrderId,
            orderNumber,
            ticket.ReopenCount,
            ticket.IsAwaitingFirstResponse,
            ticket.CreatedAtUtc,
            ticket.LastActivityAtUtc,
            ticket.ResolvedAtUtc,
            ticket.ClosedAtUtc,
            ticket.AutoCloseAtUtc,

            // Internal notes exist only for admins. A customer's request filters them out in
            // the mapper rather than relying on the caller to remember.
            TicketMapper.BuildThread(nodes, includeInternalNotes: query.IsAdmin),
            history.Select(TicketMapper.MapHistory).ToArray(),
            invoices.Select(TicketMapper.MapInvoice).ToArray());

        // The wrapper exists so a future admin-only field can be added without changing the
        // customer contract. Today both audiences get the same history.
        return Result.Success(new AdminTicketDetailResponse(detail, detail.History));
    }
}

internal sealed class GetTicketInvoicesQueryHandler(IApplicationDbContext db)
    : IQueryHandler<GetTicketInvoicesQuery, IReadOnlyList<TicketInvoiceResponse>>
{
    public async Task<Result<IReadOnlyList<TicketInvoiceResponse>>> Handle(GetTicketInvoicesQuery query, CancellationToken ct)
    {
        var owned = await db.Tickets
            .AsNoTracking()
            .Where(t => t.Id == query.TicketId)
            .Select(t => new { t.CustomerId, t.Id })
            .FirstOrDefaultAsync(ct);

        if (owned is null || (!query.IsAdmin && owned.CustomerId != query.ActorId))
            return Result.Failure<IReadOnlyList<TicketInvoiceResponse>>(
                Error.NotFound("TICKET_NOT_FOUND", "Ticket not found."));

        var invoices = await db.TicketInvoices
            .AsNoTracking()
            .Where(i => i.TicketId == query.TicketId)
            .OrderByDescending(i => i.Revision)
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<TicketInvoiceResponse>>(
            invoices.Select(TicketMapper.MapInvoice).ToArray());
    }
}

internal sealed class GetInvoiceTemplateQueryHandler(IApplicationDbContext db)
    : IQueryHandler<GetInvoiceTemplateQuery, InvoiceTemplateResponse>
{
    public async Task<Result<InvoiceTemplateResponse>> Handle(GetInvoiceTemplateQuery query, CancellationToken ct)
    {
        var template = await db.InvoiceTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == query.TemplateId, ct);

        return template is null
            ? Result.Failure<InvoiceTemplateResponse>(
                Error.NotFound("INVOICE_TEMPLATE_NOT_FOUND", "Invoice template not found."))
            : Result.Success(TicketMapper.MapTemplate(template));
    }
}

internal sealed class GetInvoiceTemplatesQueryHandler(IApplicationDbContext db)
    : IQueryHandler<GetInvoiceTemplatesQuery, IReadOnlyList<InvoiceTemplateResponse>>
{
    public async Task<Result<IReadOnlyList<InvoiceTemplateResponse>>> Handle(
        GetInvoiceTemplatesQuery query, CancellationToken ct)
    {
        var templates = await db.InvoiceTemplates
            .AsNoTracking()
            .OrderByDescending(t => t.IsDefault)
            .ThenBy(t => t.Name)
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<InvoiceTemplateResponse>>(
            templates.Select(TicketMapper.MapTemplate).ToArray());
    }
}

/// <summary>
/// Serves the rendered PDF of one invoice revision.
///
/// Ownership is checked through the parent ticket rather than through the invoice row alone,
/// so a customer cannot reach another account's document by guessing an invoice id. An invoice
/// that is still queued or has permanently failed returns a conflict that names the reason,
/// which is what the admin screen needs to show; a bare 404 would leave the operator guessing.
/// </summary>
internal sealed class GetInvoiceDocumentQueryHandler(IApplicationDbContext db)
    : IQueryHandler<GetInvoiceDocumentQuery, InvoiceDocumentResponse>
{
    public async Task<Result<InvoiceDocumentResponse>> Handle(
        GetInvoiceDocumentQuery query, CancellationToken ct)
    {
        var invoice = await db.TicketInvoices
            .AsNoTracking()
            .Where(i => i.Id == query.InvoiceId)
            .Select(i => new
            {
                i.Id,
                i.TicketId,
                i.InvoiceNumber,
                i.Revision,
                i.FileName,
                i.Status,
                i.FailureReason,
                i.GenerationAttempts,
                i.PdfContent,
                CustomerId = db.Tickets
                    .Where(t => t.Id == i.TicketId)
                    .Select(t => t.CustomerId)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync(ct);

        if (invoice is null)
            return Result.Failure<InvoiceDocumentResponse>(
                Error.NotFound("INVOICE_NOT_FOUND", "Invoice not found."));

        // NotFound rather than Forbidden, so the endpoint does not confirm the id exists.
        if (!query.IsAdmin && invoice.CustomerId != query.ActorId)
            return Result.Failure<InvoiceDocumentResponse>(
                Error.NotFound("INVOICE_NOT_FOUND", "Invoice not found."));

        if (invoice.PdfContent is not { Length: > 0 })
        {
            var reason = invoice.Status switch
            {
                TicketInvoiceStatus.Failed =>
                    invoice.FailureReason ?? "Rendering failed. Retry the invoice from the admin screen.",
                TicketInvoiceStatus.Pending =>
                    "The invoice is still being rendered. Try again shortly.",
                _ =>
                    "No document has been produced for this revision yet."
            };

            return Result.Failure<InvoiceDocumentResponse>(
                Error.Conflict("INVOICE_DOCUMENT_NOT_READY", reason));
        }

        return Result.Success(new InvoiceDocumentResponse(
            invoice.PdfContent,
            invoice.FileName,
            invoice.InvoiceNumber,
            invoice.Revision));
    }
}

internal sealed class GetSupportSettingsQueryHandler(
    SupportSettingsProvider settingsProvider,
    IOptions<SupportPolicyOptions> policyOptions)
    : IQueryHandler<GetSupportSettingsQuery, SupportSettingsResponse>
{
    public async Task<Result<SupportSettingsResponse>> Handle(GetSupportSettingsQuery query, CancellationToken ct)
    {
        var settings = await settingsProvider.GetOrCreateAsync(ct);
        return Result.Success(SupportSettingsProjection.Map(settings, policyOptions.Value));
    }
}