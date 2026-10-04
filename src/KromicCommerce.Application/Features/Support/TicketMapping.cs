namespace KromicCommerce.Application.Features.Support;

/// <summary>Entity → contract projections for the support desk.</summary>
internal static class TicketMapper
{
    // -----------------------------------------------------------------------
    // Comments
    // -----------------------------------------------------------------------

    /// <summary>
    /// Folds a flat, path-ordered comment list into a tree.
    ///
    /// The query hands rows over sorted by <c>ThreadPath</c>, so every child is guaranteed to
    /// follow its parent and a single forward pass with a stack-free "last node at depth"
    /// pointer reconstructs the hierarchy. No sorting, no recursion, no second query.
    ///
    /// Orphans — a comment whose parent is missing from the input, which can only happen if
    /// the caller filtered the set — are promoted to roots rather than dropped. Losing a
    /// customer's message because a filter excluded its parent would be worse than showing
    /// it slightly out of place.
    /// </summary>
    public static IReadOnlyList<TicketCommentResponse> BuildThread(
        IReadOnlyList<TicketCommentNode> nodes,
        bool includeInternalNotes)
    {
        var visible = nodes.Where(n => includeInternalNotes || !n.IsInternalNote).ToList();
        var builder = new Dictionary<Guid, TicketCommentResponseBuilder>(visible.Count);
        var roots = new List<TicketCommentResponseBuilder>();

        foreach (var node in visible)
        {
            var created = new TicketCommentResponseBuilder(
                new TicketCommentResponse(
                    node.Id,
                    node.ParentCommentId,
                    node.AuthorId,
                    node.AuthorName,
                    node.IsAdminAuthor,
                    node.Body,
                    node.Depth,
                    node.IsInternalNote,
                    node.CreatedAtUtc,
                    node.Attachments,
                    []));

            builder[node.Id] = created;

            // Attach to the nearest open ancestor at depth-1. Falling back to root keeps a
            // comment whose parent was filtered out or soft-orphaned visible.
            if (node.ParentCommentId.HasValue &&
                builder.TryGetValue(node.ParentCommentId.Value, out var parent))
            {
                parent.Add(created);
            }
            else
            {
                roots.Add(created);
            }
        }

        return roots.Select(r => r.Build()).ToArray();
    }

    /// <summary>Flat projection used by the comment POST response, which returns one node.</summary>
    public static TicketCommentResponse MapSingle(TicketCommentNode node) =>
        new(
            node.Id,
            node.ParentCommentId,
            node.AuthorId,
            node.AuthorName,
            node.IsAdminAuthor,
            node.Body,
            node.Depth,
            node.IsInternalNote,
            node.CreatedAtUtc,
            node.Attachments,
            []);

    // -----------------------------------------------------------------------
    // Invoices
    // -----------------------------------------------------------------------

    public static TicketInvoiceResponse MapInvoice(TicketInvoice invoice) =>
        new(
            invoice.Id,
            invoice.InvoiceNumber,
            invoice.Revision,
            invoice.Status.ToString(),
            invoice.TicketId,
            invoice.TemplateId,
            invoice.IsContentOverridden,
            invoice.QueuedAtUtc,
            invoice.GeneratedAtUtc,
            invoice.GenerationAttempts,
            invoice.FailureReason,
            invoice.FileName,
            invoice.SizeBytes,
            invoice.EmailedToCustomer,
            invoice.EmailedAtUtc,
            MapInvoiceContent(invoice.Content),
            invoice.PdfContent is { Length: > 0 });

    public static InvoiceContentResponse MapInvoiceContent(InvoiceContent content) =>
        new(
            content.IssuerName,
            content.IssuerAddress,
            content.IssuerEmail,
            content.IssuerTaxId,
            content.BillToName,
            content.BillToEmail,
            content.BillToAddress,
            content.InvoiceDateUtc,
            content.CurrencyCode,
            content.Subtotal,
            content.DiscountAmount,
            content.TaxAmount,
            content.ShippingAmount,
            content.CodFee,
            content.GrandTotal,
            content.GetLineItems()
                .Select(i => new InvoiceLineItemResponse(i.Description, i.Sku, i.Quantity, i.UnitPrice, i.LineTotal))
                .ToArray(),
            content.TicketNumber,
            content.TicketSubject,
            content.OrderNumber,
            content.Notes,
            content.Terms,
            content.FooterNote,
            content.AccentColor);

    // -----------------------------------------------------------------------
    // Templates
    // -----------------------------------------------------------------------

    public static InvoiceTemplateResponse MapTemplate(InvoiceTemplate template) =>
        new(
            template.Id,
            template.Name,
            template.Description,
            template.IsDefault,
            template.IssuerNameOverride,
            template.TaxId,
            template.Notes,
            template.Terms,
            template.FooterNote,
            template.AccentColor,
            template.ShowIssuerIdentity,
            template.ShowLineItemTable,
            template.ShowTerms,
            template.ShowNotes,
            template.CreatedAtUtc,
            template.UpdatedAtUtc);

    /// <summary>
    /// Flat read model for a list row. Assembled from a projection rather than from the
    /// tracked entity so a 100-row page does not drag the whole comment tree into memory.
    /// </summary>
    public static TicketSummaryResponse MapSummary(TicketSummaryProjection p) =>
        new(
            p.Id,
            p.TicketNumber,
            p.Subject,
            p.Status.ToString(),
            p.Priority.ToString(),
            p.CustomerId,
            p.CustomerName,
            p.CustomerEmail,
            p.AssignedAdminId,
            p.RelatedOrderId,
            p.OrderNumber,
            p.CommentCount,
            p.ReopenCount,
            p.FirstResponseAtUtc is null && p.Status == TicketStatus.Open,
            p.CreatedAtUtc,
            p.LastActivityAtUtc,
            p.ResolvedAtUtc,
            p.ClosedAtUtc,
            p.AutoCloseAtUtc,
            p.LatestInvoiceId,
            p.LatestInvoiceStatus?.ToString());

    public static TicketStatusHistoryResponse MapHistory(TicketStatusHistory h) =>
        new(
            h.Id,
            h.FromStatus?.ToString(),
            h.ToStatus.ToString(),
            h.Actor.ToString(),
            h.ActorId,
            h.ActorName,
            h.Note,
            h.OccurredAtUtc);
}

/// <summary>
/// One comment plus its resolved author name and attachments, flattened for the read path.
/// Deliberately not the entity: the tree builder needs an <c>AuthorName</c> that is not on
/// <see cref="TicketComment"/>, and pulling it from a navigation property per row would issue
/// N queries.
/// </summary>
public sealed record TicketCommentNode(
    Guid Id,
    Guid? ParentCommentId,
    Guid AuthorId,
    string AuthorName,
    bool IsAdminAuthor,
    string Body,
    int Depth,
    bool IsInternalNote,
    DateTime CreatedAtUtc,
    IReadOnlyList<TicketAttachmentResponse> Attachments);

/// <summary>Everything a ticket list row needs, projected straight out of the query.</summary>
public sealed record TicketSummaryProjection(
    Guid Id,
    string TicketNumber,
    string Subject,
    TicketStatus Status,
    TicketPriority Priority,
    Guid CustomerId,
    string CustomerName,
    string CustomerEmail,
    Guid? AssignedAdminId,
    Guid? RelatedOrderId,
    string? OrderNumber,
    int CommentCount,
    int ReopenCount,
    DateTime? FirstResponseAtUtc,
    DateTime CreatedAtUtc,
    DateTime LastActivityAtUtc,
    DateTime? ResolvedAtUtc,
    DateTime? ClosedAtUtc,
    DateTime? AutoCloseAtUtc,
    Guid? LatestInvoiceId,
    TicketInvoiceStatus? LatestInvoiceStatus);

/// <summary>Mutable accumulator so the fold can attach children before the record is frozen.</summary>
internal sealed class TicketCommentResponseBuilder(TicketCommentResponse seed)
{
    private readonly List<TicketCommentResponseBuilder> _replies = [];

    internal void Add(TicketCommentResponseBuilder reply) => _replies.Add(reply);

    internal TicketCommentResponse Build() =>
        seed with { Replies = _replies.Select(r => r.Build()).ToArray() };
}