namespace KromicCommerce.Application.Features.Support;

/// <summary>Reads ticket comments into the flattened shape the thread builder consumes.</summary>
internal static class TicketCommentLoader
{
    /// <summary>
    /// Loads a ticket's whole conversation in reading order.
    ///
    /// <para>
    /// Comments arrive from one ordered scan of <c>ThreadPath</c> — no recursion, no
    /// self-join, and no extra query per nesting level. The index on
    /// (TicketId, ThreadPath) covers the filter and the ordering together, which is what
    /// keeps a long thread cheap to read.
    /// </para>
    /// <para>
    /// Attachments are fetched as a second query keyed by the comment ids rather than as an
    /// <c>Include</c>. An <c>Include</c> of a collection makes EF emit one row per comment
    /// per attachment, so a thread of 200 comments with one image each returns 400 rows to
    /// reassemble 200 comments. Two clean result sets is both cheaper and simpler.
    /// </para>
    /// </summary>
    public static async Task<IReadOnlyList<TicketCommentNode>> LoadThreadAsync(
        IApplicationDbContext db,
        Guid ticketId,
        CancellationToken ct)
    {
        var comments = await db.TicketComments
            .AsNoTracking()
            .Where(c => c.TicketId == ticketId)
            .OrderBy(c => c.ThreadPath)
            .Select(c => new
            {
                c.Id,
                c.ParentCommentId,
                c.AuthorId,
                AuthorName = c.Author!.FullName,
                c.IsAdminAuthor,
                c.Body,
                c.Depth,
                c.InternalNote,
                c.CreatedAtUtc
            })
            .ToListAsync(ct);

        if (comments.Count == 0) return [];

        var byComment = await LoadAttachmentsAsync(
            db, comments.Select(c => c.Id).ToArray(), ct);

        return comments
            .Select(c => new TicketCommentNode(
                c.Id,
                c.ParentCommentId,
                c.AuthorId,
                c.AuthorName,
                c.IsAdminAuthor,
                c.Body,
                c.Depth,
                c.InternalNote,
                c.CreatedAtUtc,
                byComment.GetValueOrDefault(c.Id) ?? []))
            .ToArray();
    }

    /// <summary>
    /// Loads one comment for the response of a newly posted message. Falls back to a
    /// hand-built node rather than throwing: the caller has just written the row inside this
    /// transaction, so a miss would be a bug worth failing loudly on.
    /// </summary>
    public static async Task<TicketCommentNode> LoadNodeAsync(
        IApplicationDbContext db,
        Guid commentId,
        CancellationToken ct)
    {
        var comment = await db.TicketComments
            .AsNoTracking()
            .Where(c => c.Id == commentId)
            .Select(c => new
            {
                c.Id,
                c.ParentCommentId,
                c.AuthorId,
                AuthorName = c.Author!.FullName,
                c.IsAdminAuthor,
                c.Body,
                c.Depth,
                c.InternalNote,
                c.CreatedAtUtc
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException(
                $"Comment {commentId} was not found immediately after being written.");

        var byComment = await LoadAttachmentsAsync(db, [commentId], ct);

        return new TicketCommentNode(
            comment.Id,
            comment.ParentCommentId,
            comment.AuthorId,
            comment.AuthorName,
            comment.IsAdminAuthor,
            comment.Body,
            comment.Depth,
            comment.InternalNote,
            comment.CreatedAtUtc,
            byComment.GetValueOrDefault(commentId) ?? []);
    }

    private static async Task<Dictionary<Guid, IReadOnlyList<TicketAttachmentResponse>>> LoadAttachmentsAsync(
        IApplicationDbContext db,
        Guid[] commentIds,
        CancellationToken ct)
    {
        var attachments = await db.TicketAttachments
            .AsNoTracking()
            .Where(a => commentIds.Contains(a.TicketCommentId))
            .OrderBy(a => a.TicketCommentId)
            .ThenBy(a => a.SortOrder)
            .ToListAsync(ct);

        return attachments
            .GroupBy(a => a.TicketCommentId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<TicketAttachmentResponse>)g.Select(MapAttachment).ToArray());
    }

    private static TicketAttachmentResponse MapAttachment(TicketAttachment a) =>
        new(
            a.Id,
            a.Kind.ToString(),
            a.PublicId,
            a.SecureUrl,
            a.Format,
            a.ContentType,
            a.Width,
            a.Height,
            a.DurationSeconds,
            a.SizeBytes,
            a.AltText);
}