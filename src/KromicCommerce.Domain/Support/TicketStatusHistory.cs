namespace KromicCommerce.Domain.Support;

/// <summary>
/// Append-only audit record of every status transition on a ticket.
///
/// Rows are never updated or deleted, including by the background workers. That is the
/// point: the resolution rules are asymmetric (admins resolve, customers close, the system
/// closes on idle), so "who moved this ticket and when" is the only way to audit it later.
/// A note column distinguishes a human decision from the scheduled sweep.
///
/// The actor name is denormalised on purpose. A deactivated admin account is still in the
/// <c>users</c> table, but a hard-deleted one would not be — and an audit trail with holes
/// in it is worse than one small duplicate.
/// </summary>
public sealed class TicketStatusHistory : AuditableEntity
{
    /// <summary>The inactivity window used when no merchant override has been configured.</summary>
    public const int DefaultAutoCloseIdleHours = 72;

    public const int NoteMaxLength = 2000;
    public const int ActorNameMaxLength = 200;

    private TicketStatusHistory() { } // EF constructor

    public static TicketStatusHistory Create(
        Guid ticketId,
        TicketStatus? fromStatus,
        TicketStatus toStatus,
        TicketTransitionActor actor,
        Guid? actorId,
        string? actorName,
        string? note,
        DateTime? occurredAtUtc = null)
    {
        if (ticketId == Guid.Empty)
            throw new ArgumentException("Ticket id is required.", nameof(ticketId));

        return new TicketStatusHistory
        {
            TicketId = ticketId,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            Actor = actor,
            ActorId = actorId,
            ActorName = string.IsNullOrWhiteSpace(actorName)
                ? (actor == TicketTransitionActor.System ? "Automation" : null)
                : actorName.Trim()[..Math.Min(ActorNameMaxLength, actorName.Trim().Length)],
            Note = Truncate(note, NoteMaxLength),
            OccurredAtUtc = occurredAtUtc ?? DateTime.UtcNow
        };
    }

    public Guid TicketId { get; private set; }

    /// <summary>Null on the opening row, which has no predecessor.</summary>
    public TicketStatus? FromStatus { get; private set; }

    public TicketStatus ToStatus { get; private set; }

    public TicketTransitionActor Actor { get; private set; }

    /// <summary>Null for system-initiated transitions.</summary>
    public Guid? ActorId { get; private set; }

    /// <summary>Display name captured at transition time.</summary>
    public string? ActorName { get; private set; }

    /// <summary>Human explanation, or the automation's canned explanation.</summary>
    public string? Note { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    // Navigation
    public Ticket Ticket { get; private set; } = null!;

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}