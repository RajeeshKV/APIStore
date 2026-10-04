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
    /// Loads a page of list rows in two queries.
    ///
    /// The rows are filtered and ordered by the caller, which owns the paging semantics; this
    /// only resolves the joined display fields. Author name and order number are correlated
    /// projections rather than deferred navigations, so EF turns them into joins instead of
    /// N+1 round trips.
    /// </summary>
    public static IQueryable<TicketSummaryProjection> Project(IApplicationDbContext db)
    {
        return db.Tickets
            .AsNoTracking()
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
}