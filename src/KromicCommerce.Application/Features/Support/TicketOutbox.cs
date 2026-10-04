namespace KromicCommerce.Application.Features.Support;

/// <summary>
/// Outbox event types emitted by the support desk.
///
/// Every one of these is written inside the same SaveChanges call as the business change that
/// produced it, so an email can never be sent for a transition that rolled back, and a committed
/// transition can never fail to produce its notification.
///
/// The type names are matched by the Infrastructure outbox dispatcher. They are therefore a
/// published contract between two assemblies, not an internal detail: renaming one silently
/// stops the corresponding email from ever being sent, which is exactly the kind of breakage
/// that surfaces as "support emails stopped last Tuesday" and nothing else.
/// </summary>
public static class TicketOutbox
{
    /// <summary>A customer opened a ticket. Mails the configured administrator.</summary>
    public const string Created = "TicketCreated";

    /// <summary>A resolved or closed ticket was resumed. Mails the configured administrator.</summary>
    public const string Reopened = "TicketReopened";

    /// <summary>An administrator resolved a ticket. Mails the customer.</summary>
    public const string Resolved = "TicketResolved";

    /// <summary>A ticket reached Closed, by the customer or by the idle worker. Mails the customer.</summary>
    public const string Closed = "TicketClosed";

    /// <summary>Somebody posted in the thread. Mails the counterparty.</summary>
    public const string CommentPosted = "TicketCommentPosted";

    /// <summary>A worker finished rendering an invoice. Mails the customer unless the toggle is off.</summary>
    public const string InvoiceGenerated = "TicketInvoiceGenerated";
}

/// <summary>Payload of <see cref="TicketOutbox.Created"/>.</summary>
public sealed record TicketOpenedPayload(
    Guid TicketId,
    Guid CustomerId,
    string CustomerName,
    string CustomerEmail,
    string TicketNumber,
    string Subject,
    string Description,
    Guid? OrderId,
    string? OrderNumber,
    string Priority,
    DateTime OccurredAtUtc);

/// <summary>Payload of <see cref="TicketOutbox.Reopened"/>.</summary>
public sealed record TicketReopenedPayload(
    Guid TicketId,
    Guid CustomerId,
    string CustomerName,
    string CustomerEmail,
    string TicketNumber,
    string Subject,
    string FromStatus,
    int ReopenCount,
    Guid? OrderId,
    string? OrderNumber,
    DateTime OccurredAtUtc);

/// <summary>Payload of <see cref="TicketOutbox.Resolved"/>.</summary>
public sealed record TicketResolvedPayload(
    Guid TicketId,
    Guid AdminId,
    string AdminName,
    string CustomerName,
    string CustomerEmail,
    string TicketNumber,
    string Subject,
    string? ResolutionNote,
    DateTime OccurredAtUtc);

/// <summary>Payload of <see cref="TicketOutbox.Closed"/>.</summary>
public sealed record TicketClosedPayload(
    Guid TicketId,
    string CustomerName,
    string CustomerEmail,
    string TicketNumber,
    string Subject,
    string Actor,
    string? Note,
    DateTime OccurredAtUtc);

/// <summary>Payload of <see cref="TicketOutbox.CommentPosted"/>.</summary>
public sealed record TicketCommentPayload(
    Guid TicketId,
    Guid CommentId,
    Guid AuthorId,
    string AuthorName,
    string AuthorEmail,
    bool IsAdminAuthor,
    string CustomerName,
    string CustomerEmail,
    string AdminName,
    string TicketNumber,
    string Subject,
    string Body,
    string? AttachmentUrl,
    bool TicketIsClosed,
    DateTime OccurredAtUtc);

/// <summary>Payload of <see cref="TicketOutbox.InvoiceGenerated"/>.</summary>
public sealed record TicketInvoiceGeneratedPayload(
    Guid TicketId,
    Guid InvoiceId,
    Guid CustomerId,
    string CustomerName,
    string CustomerEmail,
    string InvoiceNumber,
    string TicketNumber,
    string? OrderNumber,
    string CurrencyCode,
    decimal GrandTotal,
    string FileName,
    bool EmailedAlready,
    DateTime OccurredAtUtc);