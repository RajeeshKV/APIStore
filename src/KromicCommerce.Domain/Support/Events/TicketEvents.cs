namespace KromicCommerce.Domain.Support.Events;

/// <summary>Raised when a customer opens a new ticket. Triggers the admin notification.</summary>
public sealed class TicketCreatedEvent : DomainEvent
{
    public TicketCreatedEvent(Guid ticketId, Guid customerId, string ticketNumber, string subject)
    {
        TicketId = ticketId;
        CustomerId = customerId;
        TicketNumber = ticketNumber;
        Subject = subject;
    }

    public Guid TicketId { get; }
    public Guid CustomerId { get; }
    public string TicketNumber { get; }
    public string Subject { get; }
}

/// <summary>
/// Raised when a Resolved or Closed ticket is reopened. Triggers the admin notification
/// exactly like creation — a reopened ticket is back in the working queue.
/// </summary>
public sealed class TicketReopenedEvent : DomainEvent
{
    public TicketReopenedEvent(
        Guid ticketId, Guid customerId, string ticketNumber, TicketStatus fromStatus, int reopenCount)
    {
        TicketId = ticketId;
        CustomerId = customerId;
        TicketNumber = ticketNumber;
        FromStatus = fromStatus;
        ReopenCount = reopenCount;
    }

    public Guid TicketId { get; }
    public Guid CustomerId { get; }
    public string TicketNumber { get; }
    public TicketStatus FromStatus { get; }
    public int ReopenCount { get; }
}

/// <summary>Raised when an admin resolves a ticket. This is the invoice trigger point.</summary>
public sealed class TicketResolvedEvent : DomainEvent
{
    public TicketResolvedEvent(Guid ticketId, Guid adminId, string ticketNumber, string? resolutionNote)
    {
        TicketId = ticketId;
        AdminId = adminId;
        TicketNumber = ticketNumber;
        ResolutionNote = resolutionNote;
    }

    public Guid TicketId { get; }
    public Guid AdminId { get; }
    public string TicketNumber { get; }
    public string? ResolutionNote { get; }
}

/// <summary>Raised when a ticket reaches Closed, by user confirmation or by the idle worker.</summary>
public sealed class TicketClosedEvent : DomainEvent
{
    public TicketClosedEvent(Guid ticketId, TicketTransitionActor actor, string ticketNumber)
    {
        TicketId = ticketId;
        Actor = actor;
        TicketNumber = ticketNumber;
    }

    public Guid TicketId { get; }
    public TicketTransitionActor Actor { get; }
    public string TicketNumber { get; }
}

/// <summary>Raised when any participant posts a comment. Drives LastActivityAtUtc.</summary>
public sealed class TicketCommentPostedEvent : DomainEvent
{
    public TicketCommentPostedEvent(
        Guid ticketId, Guid commentId, Guid authorId, bool isAdminAuthor)
    {
        TicketId = ticketId;
        CommentId = commentId;
        AuthorId = authorId;
        IsAdminAuthor = isAdminAuthor;
    }

    public Guid TicketId { get; }
    public Guid CommentId { get; }
    public Guid AuthorId { get; }
    public bool IsAdminAuthor { get; }
}

/// <summary>Raised the moment a new invoice revision is queued for background rendering.</summary>
public sealed class TicketInvoiceRequestedEvent : DomainEvent
{
    public TicketInvoiceRequestedEvent(Guid ticketId, Guid invoiceId, string invoiceNumber)
    {
        TicketId = ticketId;
        InvoiceId = invoiceId;
        InvoiceNumber = invoiceNumber;
    }

    public Guid TicketId { get; }
    public Guid InvoiceId { get; }
    public string InvoiceNumber { get; }
}

/// <summary>
/// Raised by the background renderer once the PDF exists. Consumed by the outbox processor
/// to mail the document when the merchant's global toggle allows it.
/// </summary>
public sealed class TicketInvoiceGeneratedEvent : DomainEvent
{
    public TicketInvoiceGeneratedEvent(Guid ticketId, Guid invoiceId, string invoiceNumber)
    {
        TicketId = ticketId;
        InvoiceId = invoiceId;
        InvoiceNumber = invoiceNumber;
    }

    public Guid TicketId { get; }
    public Guid InvoiceId { get; }
    public string InvoiceNumber { get; }
}