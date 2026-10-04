namespace KromicCommerce.Domain.Support;

/// <summary>
/// One message in a ticket's threaded dialogue.
///
/// Threads are stored as an adjacency list (<see cref="ParentCommentId"/>) plus a
/// <see cref="ThreadPath"/> materialised path. The redundancy is deliberate:
///
/// <para>
/// Loading a ticket's conversation is then a single indexed <c>ORDER BY "ThreadPath"</c>
/// over one table — the database hands back the whole tree already in depth-first
/// conversation order, with no recursive CTE, no self-joins and no in-memory sorting.
/// </para>
/// <para>
/// <see cref="ThreadPath"/> is built from fixed-width UTC ticks so that lexical ordering
/// equals chronological ordering, which is what lets a plain B-tree index do the work.
/// It also gives subtree selection for free: a descendant of X is any row whose path
/// starts with X's path.
/// </para>
///
/// <see cref="Depth"/> is a guard, not decoration. An unbounded reply chain would let one
/// customer build an unusable API payload and a pathological tree walk, so nesting past
/// <see cref="MaxDepth"/> is rejected at write time.
/// </summary>
public sealed class TicketComment : AuditableEntity
{
    public const int BodyMaxLength = 10000;
    public const int MaxDepth = 6;
    public const int MaxAttachments = 6;

    /// <summary>Separator between path segments. Not a valid Guid character.</summary>
    private const char PathSeparator = '/';

    private readonly List<TicketAttachment> _attachments = [];

    private TicketComment() { } // EF constructor

    public static TicketComment Create(
        Guid ticketId,
        Guid authorId,
        bool isAdminAuthor,
        string body,
        TicketComment? parent,
        IReadOnlyList<TicketAttachmentRequest>? attachments,
        DateTime createdAtUtc)
    {
        if (ticketId == Guid.Empty)
            throw new ArgumentException("Ticket id is required.", nameof(ticketId));
        if (authorId == Guid.Empty)
            throw new ArgumentException("Author id is required.", nameof(authorId));

        var cleanBody = ValidateBody(body);

        var parentPath = parent?.ThreadPath;
        if (parent is not null && parent.TicketId != ticketId)
            throw new ArgumentException("Parent comment belongs to a different ticket.", nameof(parent));

        var depth = parent is null ? 0 : parent.Depth + 1;
        if (depth > MaxDepth)
            throw new ArgumentException(
                $"Comment nesting cannot exceed {MaxDepth} levels.", nameof(parent));

        var segment = createdAtUtc.Ticks.ToString("D19");

        var comment = new TicketComment
        {
            TicketId = ticketId,
            AuthorId = authorId,
            IsAdminAuthor = isAdminAuthor,
            Body = cleanBody,
            ParentCommentId = parent?.Id,
            Depth = depth,
            ThreadPath = string.IsNullOrEmpty(parentPath)
                ? segment
                : parentPath + PathSeparator + segment,
            InternalNote = false
        };

        // Attachments are validated (count + kind + kind-specific limits) by the request
        // factory before reaching here, so this only enforces the aggregate ceiling.
        if (attachments is { Count: > 0 })
        {
            if (attachments.Count > MaxAttachments)
                throw new ArgumentException(
                    $"A comment may have at most {MaxAttachments} attachments.", nameof(attachments));

            for (var i = 0; i < attachments.Count; i++)
                comment._attachments.Add(TicketAttachment.Create(comment.Id, attachments[i], i));
        }

        return comment;
    }

    public Guid TicketId { get; private set; }

    /// <summary>Null for a top-level message. Self-referencing to build the thread tree.</summary>
    public Guid? ParentCommentId { get; private set; }

    public Guid AuthorId { get; private set; }

    /// <summary>
    /// Frozen at write time. Authoring role can change (a customer is never promoted) and a
    /// stored flag means a historical message cannot be re-labelled after the fact.
    /// </summary>
    public bool IsAdminAuthor { get; private set; }

    public string Body { get; private set; } = string.Empty;

    /// <summary>0 for a root message, 1 for a reply, and so on. Capped at <see cref="MaxDepth"/>.</summary>
    public int Depth { get; private set; }

    /// <summary>
    /// Slash-delimited, fixed-width UTC ticks from the thread root. Sorting by this column
    /// yields the whole conversation in reading order.
    /// </summary>
    public string ThreadPath { get; private set; } = string.Empty;

    /// <summary>
    /// Admin-only note, never shown to the customer. Kept in the same table so it can
    /// participate in the thread ordering, but filtered out of every customer read path.
    /// </summary>
    public bool InternalNote { get; private set; }

    public IReadOnlyList<TicketAttachment> Attachments => _attachments.AsReadOnly();

    // Navigation
    public Ticket Ticket { get; private set; } = null!;
    public TicketComment? Parent { get; private set; }
    public User Author { get; private set; } = null!;

    // -----------------------------------------------------------------------
    // Behaviour
    // -----------------------------------------------------------------------

    /// <summary>Edits the message body. Immutability is a deliberate non-goal for tickets.</summary>
    public void EditBody(string body) => Body = ValidateBody(body);

    /// <summary>Marks or unmarks the message as an internal note.</summary>
    public void SetInternalNote(bool isInternal)
    {
        if (isInternal && !IsAdminAuthor)
            throw new InvalidOperationException("Only an administrator can author an internal note.");
        InternalNote = isInternal;
    }

    /// <summary>True when this comment is a direct reply to <paramref name="candidate"/>.</summary>
    public bool IsDirectChildOf(TicketComment candidate) => ParentCommentId == candidate.Id;

    /// <summary>True when this comment sits anywhere under <paramref name="ancestor"/>.</summary>
    public bool IsDescendantOf(TicketComment ancestor) =>
        ThreadPath.StartsWith(ancestor.ThreadPath + PathSeparator, StringComparison.Ordinal);

    private static string ValidateBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            throw new ArgumentException("Comment body is required.", nameof(body));

        var trimmed = body.Trim();
        if (trimmed.Length > BodyMaxLength)
            throw new ArgumentException(
                $"Comment body must be {BodyMaxLength} characters or fewer.", nameof(body));

        return trimmed;
    }
}