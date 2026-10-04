using System.Text.Json;

namespace KromicCommerce.Application.Features.Support;

internal sealed class CreateTicketHandler(
    IApplicationDbContext db,
    TicketReferenceGenerator references,
    SupportSettingsProvider settingsProvider,
    IOptions<SupportPolicyOptions> policyOptions,
    ILogger<CreateTicketHandler> logger) : ICommandHandler<CreateTicketCommand, TicketSummaryResponse>
{
    public async Task<Result<TicketSummaryResponse>> Handle(CreateTicketCommand cmd, CancellationToken ct)
    {
        var customer = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == cmd.CustomerId && u.IsActive, ct);

        if (customer is null)
            return Result.Failure<TicketSummaryResponse>(
                Error.NotFound("TICKET_CUSTOMER_NOT_FOUND", "Account not found."));

        // The order link is only accepted when it is this customer's own order. Otherwise a
        // customer could point their ticket at somebody else's order and have that order's
        // line items appear on an invoice addressed to them.
        Order? order = null;
        if (cmd.OrderId is { } orderId && orderId != Guid.Empty)
        {
            order = await db.Orders
                .Include(o => o.Items)
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == orderId && o.CustomerId == cmd.CustomerId, ct);

            if (order is null)
                return Result.Failure<TicketSummaryResponse>(Error.Validation(
                    "TICKET_ORDER_NOT_FOUND",
                    "The referenced order does not exist or does not belong to you."));
        }

        var ticketNumber = await references.NextTicketNumberAsync(ct);

        var ticket = Ticket.Create(
            customer.Id,
            ticketNumber,
            cmd.Subject,
            cmd.Description,
            TicketPriority.Normal,
            order?.Id);

        // The description is the opening post. Storing it on the aggregate AND as the first
        // comment is what lets the thread be a single uniform list - the conversation has no
        // special first row for the read path to special-case.
        ticket.AddComment(customer.Id, isAdminAuthor: false, body: cmd.Description);

        db.Tickets.Add(ticket);

        var settings = await settingsProvider.GetOrCreateAsync(ct);

        // Enqueued in the same save as the ticket. If the notification cannot be sent later,
        // the outbox retries; if the ticket insert rolls back, no email is ever produced.
        if (settings.NotifyAdminOnTicketCreated)
        {
            db.OutboxEvents.Add(OutboxEvent.Create(TicketOutbox.Created, JsonSerializer.Serialize(
                new TicketOpenedPayload(
                    ticket.Id,
                    customer.Id,
                    customer.FullName,
                    customer.Email,
                    ticket.TicketNumber,
                    ticket.Subject,
                    ticket.Description,
                    order?.Id,
                    order?.OrderNumber,
                    ticket.Priority.ToString(),
                    DateTime.UtcNow))));
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Ticket {TicketNumber} opened by {CustomerId}{OrderSuffix}",
            ticket.TicketNumber,
            customer.Id,
            order is null ? string.Empty : $" for order {order.OrderNumber}");

        if (!policyOptions.Value.IsAdminNotificationConfigured)
        {
            // Not an error. Ticket creation must never fail because an operational mailbox
            // was left unconfigured, but an admin who has not set the address should know
            // that nobody is being told about new tickets.
            logger.LogWarning(
                "Ticket {TicketNumber} created but Support:AdminNotificationEmail is not configured; " +
                "no administrative notification was queued.",
                ticket.TicketNumber);
        }

        return Result.Success(new TicketSummaryResponse(
            ticket.Id,
            ticket.TicketNumber,
            ticket.Subject,
            ticket.Status.ToString(),
            ticket.Priority.ToString(),
            customer.Id,
            customer.FullName,
            customer.Email,
            ticket.AssignedAdminId,
            ticket.RelatedOrderId,
            order?.OrderNumber,
            1,
            ticket.ReopenCount,
            ticket.IsAwaitingFirstResponse,
            ticket.CreatedAtUtc,
            ticket.LastActivityAtUtc,
            ticket.ResolvedAtUtc,
            ticket.ClosedAtUtc,
            ticket.AutoCloseAtUtc,
            ticket.LatestInvoiceId,
            null));
    }
}

internal sealed class AddTicketCommentHandler(
    IApplicationDbContext db,
    SupportSettingsProvider settingsProvider,
    ILogger<AddTicketCommentHandler> logger) : ICommandHandler<AddTicketCommentCommand, TicketCommentResponse>
{
    public async Task<Result<TicketCommentResponse>> Handle(AddTicketCommentCommand cmd, CancellationToken ct)
    {
        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == cmd.TicketId, ct);
        if (ticket is null)
            return Result.Failure<TicketCommentResponse>(
                Error.NotFound("TICKET_NOT_FOUND", "Ticket not found."));

        // Ownership is checked before anything else so a non-owner gets NotFound rather than
        // Forbidden - a 403 would confirm that a given ticket id exists.
        if (!cmd.IsAdmin && !ticket.IsOwnedBy(cmd.ActorId))
            return Result.Failure<TicketCommentResponse>(
                Error.NotFound("TICKET_NOT_FOUND", "Ticket not found."));

        var author = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == cmd.ActorId && u.IsActive, ct);

        if (author is null)
            return Result.Failure<TicketCommentResponse>(
                Error.Unauthorized("TICKET_AUTHOR_NOT_FOUND", "Account not found."));

        // A customer replying to a closed ticket is asking for help again, not commenting on
        // a historical record. Treating it as a reopen keeps the notification and the audit
        // trail honest; a comment that silently does nothing would lose the message.
        if (!cmd.IsAdmin && ticket.Status == TicketStatus.Closed)
            db.TicketStatusHistory.Add(
            ticket.Reopen(ticket.CustomerId, $"Customer replied on a closed ticket: {Summarise(cmd.Body)}"));

        TicketComment? parent = null;
        if (cmd.ParentCommentId is { } parentId)
        {
            parent = await db.TicketComments
                .FirstOrDefaultAsync(c => c.Id == parentId && c.TicketId == ticket.Id, ct);

// Replying under an administrator's private note would surface its existence to a
            // customer, so it is refused with the same error as an unknown id.
            if (parent is null || (!cmd.IsAdmin && parent.InternalNote))
                return Result.Failure<TicketCommentResponse>(Error.NotFound(
                    "TICKET_COMMENT_NOT_FOUND", "The comment you are replying to was not found."));
        }

        var settings = await settingsProvider.GetOrCreateAsync(ct);
        var attachments = ClampAttachments(cmd.Attachments, settings.MaxAttachmentsPerComment);

        TicketComment comment;
        try
        {
            // The internal-note flag goes through the aggregate so the "only an administrator
            // may author a note" rule is enforced on the entity, not only in the validator.
            comment = ticket.AddComment(
                author.Id, cmd.IsAdmin, cmd.Body, parent, attachments, cmd.IsInternalNote);
        }
        catch (ArgumentException ex)
        {
            // Domain guards (nesting depth, attachment ceiling, body length) surface as a
            // validation error rather than a 500.
            return Result.Failure<TicketCommentResponse>(Error.Validation("TICKET_COMMENT_INVALID", ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure<TicketCommentResponse>(Error.Validation("TICKET_COMMENT_INVALID", ex.Message));
        }

        // Explicitly Added, not merely appended to the parent's collection.
        //
        // Entity.Id assigns a client-side Guid, but EF still treats the key as store-generated,
        // so a new dependent reached through a tracked parent's navigation is discovered as an
        // *existing* row and marked Modified. Saving then fails with a concurrency exception
        // ("expected to affect 1 row(s)") because no such row exists — which is exactly the path
        // taken whenever a comment is posted to an already-persisted ticket. Handlers that build
        // a whole aggregate and add the root work fine, because Add() on the root propagates.
        db.TicketComments.Add(comment);

        // Notify the other party. The counterparty is resolved by role, not by "whoever did
        // not write this", so an admin note never mails the customer.
        var notifyCustomer = cmd.IsAdmin && !cmd.IsInternalNote;
        var notifyAdmin = !cmd.IsAdmin && settings.NotifyAdminOnTicketReopened;

        if (notifyCustomer || notifyAdmin)
        {
            db.OutboxEvents.Add(OutboxEvent.Create(TicketOutbox.CommentPosted, JsonSerializer.Serialize(
                await BuildPayloadAsync(ticket, comment, author, isAdminAuthor: cmd.IsAdmin, ct))));
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Comment {CommentId} added to ticket {TicketNumber} by {AuthorId} (admin: {IsAdmin}, internal: {IsInternal})",
            comment.Id, ticket.TicketNumber, author.Id, cmd.IsAdmin, comment.InternalNote);

        return Result.Success(TicketMapper.MapSingle(
            await TicketCommentLoader.LoadNodeAsync(db, comment.Id, ct)));
    }

    private static IReadOnlyList<TicketAttachmentRequest> ClampAttachments(
        IReadOnlyList<TicketAttachmentRequest>? attachments, int configuredMax)
    {
        if (attachments is null || attachments.Count == 0) return [];

        // The merchant may configure a lower ceiling than the domain constant. Never a higher
        // one - the domain guard would reject the write anyway, and failing the request with
        // a clear limit beats a 500 from an argument exception.
        var max = Math.Min(configuredMax, TicketComment.MaxAttachments);
        return attachments.Count > max ? attachments.Take(max).ToArray() : attachments;
    }

    private static string Summarise(string body) =>
        body.Length <= 120 ? body : body[..117] + "...";

    private async Task<TicketCommentPayload> BuildPayloadAsync(
        Ticket ticket, TicketComment comment, User author, bool isAdminAuthor, CancellationToken ct)
    {
        var customer = await db.Users.AsNoTracking()
            .FirstAsync(u => u.Id == ticket.CustomerId, ct);

        var adminName = ticket.AssignedAdminId is { } adminId
            ? await db.Users.AsNoTracking()
                .Where(u => u.Id == adminId)
                .Select(u => u.FullName)
                .FirstOrDefaultAsync(ct)
            : null;

        var attachmentUrl = comment.Attachments.FirstOrDefault()?.SecureUrl;

        return new TicketCommentPayload(
            ticket.Id,
            comment.Id,
            author.Id,
            author.FullName,
            author.Email,
            isAdminAuthor,
            customer.FullName,
            customer.Email,
            adminName ?? "Support",
            ticket.TicketNumber,
            ticket.Subject,
            comment.Body,
            attachmentUrl,
            ticket.Status == TicketStatus.Closed,
            DateTime.UtcNow);
    }
}

internal sealed class CloseTicketHandler(
    IApplicationDbContext db,
    ILogger<CloseTicketHandler> logger) : ICommandHandler<CloseTicketCommand, TicketSummaryResponse>
{
    public async Task<Result<TicketSummaryResponse>> Handle(CloseTicketCommand cmd, CancellationToken ct)
    {
        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == cmd.TicketId, ct);
        if (ticket is null || !ticket.IsOwnedBy(cmd.CustomerId))
            return Result.Failure<TicketSummaryResponse>(
                Error.NotFound("TICKET_NOT_FOUND", "Ticket not found."));

        // Only a resolved ticket can be closed. Closing an Open ticket would silently discard
        // an unanswered question.
        if (!ticket.CanClose)
            return Result.Failure<TicketSummaryResponse>(Error.Conflict(
                "TICKET_NOT_RESOLVED",
                $"Only a resolved ticket can be closed; this one is {ticket.Status}."));

        // The appended history row is tracked explicitly; see the note in AddTicketCommentHandler
        // about why a new child of an already-tracked aggregate is not inserted by change tracking.
        db.TicketStatusHistory.Add(ticket.Close(TicketTransitionActor.User, cmd.CustomerId, cmd.Note));

        var customer = await db.Users.AsNoTracking()
            .FirstAsync(u => u.Id == ticket.CustomerId, ct);

        db.OutboxEvents.Add(OutboxEvent.Create(TicketOutbox.Closed, JsonSerializer.Serialize(
            new TicketClosedPayload(
                ticket.Id,
                customer.FullName,
                customer.Email,
                ticket.TicketNumber,
                ticket.Subject,
                TicketTransitionActor.User.ToString(),
                cmd.Note,
                DateTime.UtcNow))));

        var summary = await TicketReadModel.LoadSummaryAsync(db, ticket, ct);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Ticket {TicketNumber} closed by its customer", ticket.TicketNumber);
        return Result.Success(summary);
    }
}

internal sealed class ReopenTicketHandler(
    IApplicationDbContext db,
    SupportSettingsProvider settingsProvider,
    ILogger<ReopenTicketHandler> logger) : ICommandHandler<ReopenTicketCommand, TicketSummaryResponse>
{
    public async Task<Result<TicketSummaryResponse>> Handle(ReopenTicketCommand cmd, CancellationToken ct)
    {
        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == cmd.TicketId, ct);
        if (ticket is null || !ticket.IsOwnedBy(cmd.CustomerId))
            return Result.Failure<TicketSummaryResponse>(
                Error.NotFound("TICKET_NOT_FOUND", "Ticket not found."));

        if (!ticket.CanReopen)
            return Result.Failure<TicketSummaryResponse>(Error.Conflict(
                "TICKET_ALREADY_OPEN",
                $"This ticket is already open and cannot be reopened."));

        var customer = await db.Users.AsNoTracking()
            .FirstAsync(u => u.Id == ticket.CustomerId, ct);

        var orderNumber = ticket.RelatedOrderId is { } orderId
            ? await db.Orders.AsNoTracking()
                .Where(o => o.Id == orderId)
                .Select(o => o.OrderNumber)
                .FirstOrDefaultAsync(ct)
            : null;

        // Explicitly tracked for the same reason as the close path: the transition appends a history
        // row to an already-persisted ticket.
        db.TicketStatusHistory.Add(ticket.Reopen(cmd.CustomerId, cmd.Reason));

        var settings = await settingsProvider.GetOrCreateAsync(ct);

        if (settings.NotifyAdminOnTicketReopened)
        {
            db.OutboxEvents.Add(OutboxEvent.Create(TicketOutbox.Reopened, JsonSerializer.Serialize(
                new TicketReopenedPayload(
                    ticket.Id,
                    customer.Id,
                    customer.FullName,
                    customer.Email,
                    ticket.TicketNumber,
                    ticket.Subject,
                    "Resolved",
                    ticket.ReopenCount,
                    ticket.RelatedOrderId,
                    orderNumber,
                    DateTime.UtcNow))));
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Ticket {TicketNumber} reopened by its customer (attempt {ReopenCount})",
            ticket.TicketNumber, ticket.ReopenCount);

        return Result.Success(await TicketReadModel.LoadSummaryAsync(db, ticket, ct));
    }
}

internal sealed class SetTicketPriorityHandler(
    IApplicationDbContext db,
    ILogger<SetTicketPriorityHandler> logger) : ICommandHandler<SetTicketPriorityCommand, TicketSummaryResponse>
{
    public async Task<Result<TicketSummaryResponse>> Handle(SetTicketPriorityCommand cmd, CancellationToken ct)
    {
        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == cmd.TicketId, ct);
        if (ticket is null)
            return Result.Failure<TicketSummaryResponse>(
                Error.NotFound("TICKET_NOT_FOUND", "Ticket not found."));

        ticket.SetPriority(cmd.Priority);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Ticket {TicketNumber} priority set to {Priority}", ticket.TicketNumber, cmd.Priority);

        return Result.Success(await TicketReadModel.LoadSummaryAsync(db, ticket, ct));
    }
}

internal sealed class AssignTicketHandler(
    IApplicationDbContext db,
    ILogger<AssignTicketHandler> logger) : ICommandHandler<AssignTicketCommand, TicketSummaryResponse>
{
    public async Task<Result<TicketSummaryResponse>> Handle(AssignTicketCommand cmd, CancellationToken ct)
    {
        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == cmd.TicketId, ct);
        if (ticket is null)
            return Result.Failure<TicketSummaryResponse>(
                Error.NotFound("TICKET_NOT_FOUND", "Ticket not found."));

        ticket.AssignTo(cmd.AdminId);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Ticket {TicketNumber} assigned to {AdminId}", ticket.TicketNumber, cmd.AdminId);

        return Result.Success(await TicketReadModel.LoadSummaryAsync(db, ticket, ct));
    }
}