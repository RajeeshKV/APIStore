namespace KromicCommerce.Application.Features.Support;

/// <summary>Read-model helpers shared by the command and query handlers.</summary>
internal static class TicketReadModel
{
    /// <summary>
    /// Builds the list-row projection for a ticket that the caller has already loaded.
    ///
    /// Comment count and latest invoice status are separate scalar lookups rather than
    /// navigation-property counts. Counting through <c>Comments.Count</c> would issue one
    /// query per ticket, turning a 100-row page into 200 round trips.
    /// </summary>
    public static async Task<TicketSummaryResponse> LoadSummaryAsync(
        IApplicationDbContext db,
        Ticket ticket,
        CancellationToken ct)
    {
        var customer = await db.Users.AsNoTracking()
            .Where(u => u.Id == ticket.CustomerId)
            .Select(u => new { u.Id, u.FullName, u.Email })
            .FirstAsync(ct);

        var orderNumber = ticket.RelatedOrderId is { } orderId
            ? await db.Orders.AsNoTracking()
                .Where(o => o.Id == orderId)
                .Select(o => o.OrderNumber)
                .FirstOrDefaultAsync(ct)
            : null;

        var commentCount = await db.TicketComments
            .CountAsync(c => c.TicketId == ticket.Id, ct);

        var latestInvoiceStatus = await db.TicketInvoices
            .AsNoTracking()
            .Where(i => i.TicketId == ticket.Id)
            .OrderByDescending(i => i.Revision)
            .Select(i => (TicketInvoiceStatus?)i.Status)
            .FirstOrDefaultAsync(ct);

        return TicketMapper.MapSummary(new TicketSummaryProjection(
            ticket.Id,
            ticket.TicketNumber,
            ticket.Subject,
            ticket.Status,
            ticket.Priority,
            customer.Id,
            customer.FullName,
            customer.Email,
            ticket.AssignedAdminId,
            ticket.RelatedOrderId,
            orderNumber,
            commentCount,
            ticket.ReopenCount,
            ticket.FirstResponseAtUtc,
            ticket.CreatedAtUtc,
            ticket.LastActivityAtUtc,
            ticket.ResolvedAtUtc,
            ticket.ClosedAtUtc,
            ticket.AutoCloseAtUtc,
            ticket.LatestInvoiceId,
            latestInvoiceStatus));
    }

    /// <summary>
    /// Builds the enriched list-row projection over an already-filtered ticket query.
    ///
    /// Author name and order number come from navigations, which EF turns into joins, and the
    /// comment count and latest invoice status are correlated subqueries — one round trip for
    /// the whole page instead of one per ticket.
    ///
    /// <para>
    /// Takes <see cref="IQueryable{Ticket}"/> rather than the context so that callers can apply
    /// their page filter to the ticket table <em>before</em> the projection. Adding the filter
    /// afterwards — <c>Project(db).Where(p =&gt; ids.Contains(p.Id))</c> — reintroduces exactly
    /// the failure this shape exists to avoid.
    /// </para>
    /// <para>
    /// The result is read-only: never filter, count or order this projection. Once EF has the
    /// join it cannot reduce <c>new TicketSummaryProjection(...).SomeMember</c> back to a
    /// column, and raises "could not be translated".
    /// </para>
    /// </summary>
    public static IQueryable<TicketSummaryProjection> Project(
        IApplicationDbContext db, IQueryable<Ticket> source)
    {
        return source
            .Select(t => new TicketSummaryProjection(
                t.Id,
                t.TicketNumber,
                t.Subject,
                t.Status,
                t.Priority,
                t.CustomerId,
                t.Customer!.FullName,
                t.Customer.Email,
                t.AssignedAdminId,
                t.RelatedOrderId,
                t.RelatedOrder != null ? t.RelatedOrder.OrderNumber : null,
                t.Comments.Count,
                t.ReopenCount,
                t.FirstResponseAtUtc,
                t.CreatedAtUtc,
                t.LastActivityAtUtc,
                t.ResolvedAtUtc,
                t.ClosedAtUtc,
                t.AutoCloseAtUtc,
                t.LatestInvoiceId,

                // Correlated subquery rather than a navigation collection: one round trip for
                // the whole page instead of one per ticket. Null when no invoice exists.
                db.TicketInvoices
                    .Where(i => i.TicketId == t.Id)
                    .OrderByDescending(i => i.Revision)
                    .Select(i => (TicketInvoiceStatus?)i.Status)
                    .FirstOrDefault()));
    }

    /// <summary>
    /// Paginates an already-filtered ticket query and hydrates the page.
    ///
    /// Three round trips — count, page of ids, enriched rows. Filtering, counting and ordering
    /// run over <see cref="IQueryable{Ticket}"/> rather than over <see cref="Project"/>, for the
    /// translation reason documented there. Cross-entity search is expressed by the caller as
    /// <c>Any</c> subqueries, which translate cleanly against the ticket table.
    ///
    /// The split also keeps the list cheaper than a single wide query: the comment count and
    /// latest invoice status are computed for one page instead of for every matching row.
    ///
    /// Returns the rows in the order paging decided, which a separate <c>WHERE Id IN (...)</c>
    /// does not preserve.
    /// </summary>
    public static async Task<(TicketSummaryProjection[] Rows, int Total)> LoadPageAsync(
        IQueryable<Ticket> filtered,
        IApplicationDbContext db,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var total = await filtered.CountAsync(ct);

        var ids = await filtered
            // Most recently touched first: an administrator reopening or answering an old ticket
            // expects it at the top of the queue. Id is a deterministic tiebreak so paging cannot
            // repeat or skip a row when two tickets share a timestamp.
            .OrderByDescending(t => t.LastActivityAtUtc)
            .ThenByDescending(t => t.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => t.Id)
            .ToListAsync(ct);

        if (ids.Count == 0)
            return ([], total);

        var rows = await Project(db, filtered.Where(t => ids.Contains(t.Id)))
            .ToListAsync(ct);

        // The id query decided the order; IN does not preserve it, so restore it explicitly.
        var rank = ids.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        return (rows.OrderBy(r => rank[r.Id]).ToArray(), total);
    }
}