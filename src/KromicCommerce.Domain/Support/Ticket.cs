using KromicCommerce.Domain.Support.Events;

namespace KromicCommerce.Domain.Support;

/// <summary>
/// A customer support conversation: one subject line, a threaded dialogue, and a full
/// audit trail of every status change.
///
/// Lifecycle ownership is asymmetric and is enforced here rather than in validators:
///
///   - Creation and reopening are the customer's.
///   - <b>Resolving is the administrator's</b> — a customer can never mark their own
///     problem fixed, because the whole invoice pipeline hangs off this transition.
///   - Closing a resolved ticket is the customer's confirmation, or the idle worker acting
///     on their behalf after <c>AutoCloseIdleHours</c> of silence.
///   - Reopening is the customer's, from either terminal state.
///
/// Every transition writes a <see cref="TicketStatusHistory"/> row in the same save, so the
/// state log cannot drift from the current status even if a worker crashes midway.
///
/// TIME-BASED CLOSURE
/// The deadline lives in <see cref="AutoCloseAtUtc"/> rather than being computed at read
/// time from <see cref="LastActivityAtUtc"/>. That lets the auto-close worker issue a plain
/// indexed range scan every cycle instead of loading candidate rows and doing date maths.
/// It is re-stamped from <b>user</b> activity only: an admin note 80 hours after resolution
/// does not keep a conversation open that the customer has abandoned.
/// </summary>
public sealed class Ticket : AuditableEntity
{
    public const int SubjectMaxLength = 200;
    public const int DescriptionMaxLength = 8000;
    public const int NoteMaxLength = 2000;

    private readonly List<TicketComment> _comments = [];
    private readonly List<TicketStatusHistory> _history = [];

    private Ticket() { } // EF constructor

    public static Ticket Create(
        Guid customerId,
        string ticketNumber,
        string subject,
        string description,
        TicketPriority priority = TicketPriority.Normal,
        Guid? orderId = null,
        DateTime? now = null)
    {
        if (customerId == Guid.Empty)
            throw new ArgumentException("Customer id is required.", nameof(customerId));
        if (string.IsNullOrWhiteSpace(ticketNumber))
            throw new ArgumentException("Ticket number is required.", nameof(ticketNumber));

        var (cleanSubject, cleanDescription) = ValidateText(subject, description);

        var at = now ?? DateTime.UtcNow;

        var ticket = new Ticket
        {
            CustomerId = customerId,
            TicketNumber = ticketNumber.Trim(),
            Subject = cleanSubject,
            Description = cleanDescription,
            Status = TicketStatus.Open,
            Priority = priority,
            RelatedOrderId = orderId == Guid.Empty ? null : orderId,
            ReopenCount = 0,
            LastActivityAtUtc = at,
            LastUserActivityAtUtc = at,
            AutoCloseIdleHours = TicketStatusHistory.DefaultAutoCloseIdleHours
        };

        ticket.RecordTransition(null, TicketStatus.Open, TicketTransitionActor.User,
            customerId, null, "Ticket opened.");
        ticket.RaiseDomainEvent(new TicketCreatedEvent(
            ticket.Id, customerId, ticket.TicketNumber, ticket.Subject));

        return ticket;
    }

    // -----------------------------------------------------------------------
    // Fields
    // -----------------------------------------------------------------------

    /// <summary>Human-readable reference, e.g. <c>TKT-2026-000123</c>. Unique and immutable.</summary>
    public string TicketNumber { get; private set; } = string.Empty;

    /// <summary>The customer who opened the thread. Only this account may reopen it.</summary>
    public Guid CustomerId { get; private set; }

    public string Subject { get; private set; } = string.Empty;

    /// <summary>The opening post. Also persisted as the thread's first comment.</summary>
    public string Description { get; private set; } = string.Empty;

    public TicketStatus Status { get; private set; }
    public TicketPriority Priority { get; private set; }

    /// <summary>Optional order this conversation is about. Supplies the invoice line data.</summary>
    public Guid? RelatedOrderId { get; private set; }

    public Guid? AssignedAdminId { get; private set; }

    /// <summary>First admin reply. Drives the "needs attention" queue ordering.</summary>
    public DateTime? FirstResponseAtUtc { get; private set; }

    public DateTime? ResolvedAtUtc { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public DateTime? LastReopenedAtUtc { get; private set; }

    /// <summary>How many times this ticket has been reopened. Flags chronic issues.</summary>
    public int ReopenCount { get; private set; }

    /// <summary>Last activity by anybody — used for list sorting and "updated" display.</summary>
    public DateTime LastActivityAtUtc { get; private set; }

    /// <summary>Last activity by the customer. The auto-close window counts from here.</summary>
    public DateTime LastUserActivityAtUtc { get; private set; }

    /// <summary>
    /// When the idle worker may close this ticket. Non-null only while Resolved.
    /// Persisted rather than derived so the worker's query stays index-friendly.
    /// </summary>
    public DateTime? AutoCloseAtUtc { get; private set; }

    /// <summary>
    /// Inactivity window, in hours, that the deadline above is built from. Captured on the
    /// ticket when it is resolved so that a later customer reply can re-arm the deadline
    /// with the same window the administrator's store settings were using at the time.
    /// </summary>
    public int AutoCloseIdleHours { get; private set; } = TicketStatusHistory.DefaultAutoCloseIdleHours;

    /// <summary>Sets the inactivity window used by the next resolution on this ticket.</summary>
    public void SetAutoCloseIdleHours(int hours)
    {
        if (hours < 1) throw new ArgumentOutOfRangeException(nameof(hours), "Idle window must be at least 1 hour.");
        AutoCloseIdleHours = hours;
    }

    /// <summary>The most recently generated (or latest) invoice revision for this ticket.</summary>
    public Guid? LatestInvoiceId { get; private set; }

    public IReadOnlyList<TicketComment> Comments => _comments.AsReadOnly();
    public IReadOnlyList<TicketStatusHistory> History => _history.AsReadOnly();

    // Navigation
    public User Customer { get; private set; } = null!;

    /// <summary>
    /// The order this conversation is about, when there is one. SetNull on delete so a
    /// support history survives the removal of the order it referenced — the conversation is
    /// still the record of what the customer was told.
    /// </summary>
    public Order? RelatedOrder { get; private set; }

    // -----------------------------------------------------------------------
    // Transition capability
    // Exposed so a handler can translate an illegal transition into a 409 instead of
    // catching InvalidOperationException and guessing at the intent.
    // -----------------------------------------------------------------------

    public bool CanResolve => Status == TicketStatus.Open;
    public bool CanClose => Status == TicketStatus.Resolved;
    public bool CanReopen => Status is TicketStatus.Resolved or TicketStatus.Closed;

    /// <summary>True when the idle worker may close this ticket at <paramref name="nowUtc"/>.</summary>
    public bool IsAutoCloseDue(DateTime nowUtc) =>
        Status == TicketStatus.Resolved && AutoCloseAtUtc.HasValue && AutoCloseAtUtc.Value <= nowUtc;

    /// <summary>True until an admin has replied — drives the unanswered-ticket queue.</summary>
    public bool IsAwaitingFirstResponse => FirstResponseAtUtc is null && Status == TicketStatus.Open;

    // -----------------------------------------------------------------------
    // State machine
    // -----------------------------------------------------------------------

    /// <summary>Admin action: the issue has been handled. Fires the invoice trigger.</summary>
    /// <returns>
    /// The history row this transition appended. Callers must track it explicitly
    /// (<c>db.TicketStatusHistory.Add(...)</c>): a new row appended to a ticket that is already
    /// tracked is discovered as an <i>existing</i> row, because <see cref="Entity.Id"/> assigns
    /// the client-side key while EF still treats it as store-generated. Saving without this
    /// raises a concurrency exception against a row that was never inserted.
    /// </returns>
    public TicketStatusHistory Resolve(
        Guid adminId,
        string? resolutionNote = null,
        int autoCloseIdleHours = TicketStatusHistory.DefaultAutoCloseIdleHours,
        DateTime? now = null)
    {
        RequireStatus(TicketStatus.Open, nameof(Resolve));

        var at = now ?? DateTime.UtcNow;

        // The window is captured onto the ticket so a later customer reply re-arms the
        // deadline with the same hours the merchant configured, not today's default.
        SetAutoCloseIdleHours(autoCloseIdleHours);

        Status = TicketStatus.Resolved;
        ResolvedAtUtc = at;
        FirstResponseAtUtc ??= at;
        AutoCloseAtUtc = at.AddHours(AutoCloseIdleHours);
        LastActivityAtUtc = at;

        RecordTransition(TicketStatus.Open, TicketStatus.Resolved, TicketTransitionActor.Admin,
            adminId, Truncate(resolutionNote, NoteMaxLength), "Marked as resolved.");

        RaiseDomainEvent(new TicketResolvedEvent(Id, adminId, TicketNumber, resolutionNote));

        return _history[^1];
    }

    /// <summary>
    /// Closes a resolved ticket. Reached either from the user's confirmation click
    /// (<paramref name="actor"/> = User) or from the idle worker (System).
    /// </summary>
    /// <returns>The history row this transition appended. See <see cref="Resolve"/>.</returns>
    public TicketStatusHistory Close(
        TicketTransitionActor actor,
        Guid? actorId = null,
        string? note = null,
        DateTime? now = null)
    {
        RequireStatus(TicketStatus.Resolved, nameof(Close));

        if (actor == TicketTransitionActor.Admin)
            throw new InvalidOperationException(
                "Admins do not close tickets; the customer confirms closure or the idle worker applies it.");

        var at = now ?? DateTime.UtcNow;

        Status = TicketStatus.Closed;
        ClosedAtUtc = at;
        AutoCloseAtUtc = null;
        LastActivityAtUtc = at;

        RecordTransition(TicketStatus.Resolved, TicketStatus.Closed, actor, actorId,
            Truncate(note, NoteMaxLength),
            actor == TicketTransitionActor.System
                ? "Automatically closed after the inactivity window elapsed."
                : "Closed by the customer.");

        RaiseDomainEvent(new TicketClosedEvent(Id, actor, TicketNumber));

        return _history[^1];
    }

    /// <summary>
    /// Returns a resolved or closed ticket to Open and extends the conversation.
    /// Customer-only. Re-arms the idle clock only after the next resolution.
    /// </summary>
    /// <returns>The history row this transition appended. See <see cref="Resolve"/>.</returns>
    public TicketStatusHistory Reopen(Guid customerId, string? reason = null, DateTime? now = null)
    {
        if (!CanReopen)
            throw new InvalidOperationException($"Ticket cannot be reopened from {Status}.");

        var at = now ?? DateTime.UtcNow;
        var from = Status;

        Status = TicketStatus.Open;
        ReopenCount++;
        LastReopenedAtUtc = at;
        LastActivityAtUtc = at;
        LastUserActivityAtUtc = at;
        AutoCloseAtUtc = null;

        RecordTransition(from, TicketStatus.Open, TicketTransitionActor.User, customerId,
            Truncate(reason, NoteMaxLength),
            ReopenCount > 1
                ? $"Reopened by the customer (attempt {ReopenCount})."
                : "Reopened by the customer.");

        // Reopening puts the ticket back in the working queue, so the administrator is
        // notified exactly as on creation.
        RaiseDomainEvent(new TicketReopenedEvent(Id, customerId, TicketNumber, from, ReopenCount));

        return _history[^1];
    }

    // -----------------------------------------------------------------------
    // Activity / threading
    // -----------------------------------------------------------------------

    /// <summary>
    /// Appends a comment to the thread. Any comment bumps <see cref="LastActivityAtUtc"/>;
    /// only a customer comment moves the auto-close deadline, because the deadline exists
    /// to detect a silent customer and nothing else.
    /// </summary>
    /// <param name="isInternal">
    /// Marks the message as an administrator's private note. Rejected for a customer author —
    /// the entity enforces it rather than relying on the route policy, so a new call site
    /// cannot leak an internal note into a customer's view.
    /// </param>
    public TicketComment AddComment(
        Guid authorId,
        bool isAdminAuthor,
        string body,
        TicketComment? parent = null,
        IReadOnlyList<TicketAttachmentRequest>? attachments = null,
        bool isInternal = false,
        DateTime? now = null)
    {
        if (parent is not null && parent.TicketId != Id)
            throw new ArgumentException("Parent comment belongs to a different ticket.", nameof(parent));

        if (isInternal && !isAdminAuthor)
            throw new InvalidOperationException("Only an administrator can author an internal note.");

        var at = now ?? DateTime.UtcNow;
        var comment = TicketComment.Create(Id, authorId, isAdminAuthor, body, parent, attachments, at);

        if (isInternal)
            comment.SetInternalNote(true);

        _comments.Add(comment);

        LastActivityAtUtc = at;

        if (!isAdminAuthor)
        {
            LastUserActivityAtUtc = at;

            // The customer is still talking, so a resolved ticket must not be closed out
            // from under them. Only reachable while Resolved — a closed ticket stays closed
            // until an explicit reopen, which is a separate, deliberate action.
            if (Status == TicketStatus.Resolved)
                AutoCloseAtUtc = at.AddHours(AutoCloseIdleHours);
        }

        if (isAdminAuthor)
            FirstResponseAtUtc ??= at;

        RaiseDomainEvent(new TicketCommentPostedEvent(Id, comment.Id, authorId, isAdminAuthor));
        return comment;
    }

    /// <summary>Assigns an administrator. The first assignment also counts as a response.</summary>
    public void AssignTo(Guid adminId, DateTime? now = null)
    {
        AssignedAdminId = adminId;
        LastActivityAtUtc = now ?? DateTime.UtcNow;
        FirstResponseAtUtc ??= LastActivityAtUtc;
    }

    public void SetPriority(TicketPriority priority, DateTime? now = null)
    {
        Priority = priority;
        LastActivityAtUtc = now ?? DateTime.UtcNow;
    }

    public void SetLatestInvoice(Guid invoiceId) => LatestInvoiceId = invoiceId;

    /// <summary>True when this conversation belongs to the given customer.</summary>
    public bool IsOwnedBy(Guid customerId) => CustomerId == customerId;

    /// <summary>
    /// Appends a history row. Public because the auto-close worker and the invoice
    /// pipeline add entries that are not triggered by a domain state change.
    /// </summary>
    public TicketStatusHistory RecordTransition(
        TicketStatus? from,
        TicketStatus to,
        TicketTransitionActor actor,
        Guid? actorId,
        string? note,
        string? actorName = null)
    {
        var entry = TicketStatusHistory.Create(Id, from, to, actor, actorId, actorName, note);
        _history.Add(entry);
        return entry;
    }

    // -----------------------------------------------------------------------
    // Guards
    // -----------------------------------------------------------------------

    private void RequireStatus(TicketStatus expected, string operation)
    {
        if (Status != expected)
            throw new InvalidOperationException(
                $"Ticket cannot be {operation.ToLowerInvariant()} from {Status}; expected {expected}.");
    }

    private static (string Subject, string Description) ValidateText(string subject, string description)
    {
        if (string.IsNullOrWhiteSpace(subject))
            throw new ArgumentException("Ticket subject is required.", nameof(subject));
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Ticket description is required.", nameof(description));

        var cleanSubject = subject.Trim();
        if (cleanSubject.Length > SubjectMaxLength)
            throw new ArgumentException(
                $"Ticket subject must be {SubjectMaxLength} characters or fewer.", nameof(subject));

        var cleanDescription = description.Trim();
        if (cleanDescription.Length > DescriptionMaxLength)
            throw new ArgumentException(
                $"Ticket description must be {DescriptionMaxLength} characters or fewer.", nameof(description));

        return (cleanSubject, cleanDescription);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}