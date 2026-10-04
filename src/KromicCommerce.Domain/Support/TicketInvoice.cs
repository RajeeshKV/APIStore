namespace KromicCommerce.Domain.Support;

/// <summary>A single billable line captured onto an invoice at the moment it was queued.</summary>
public sealed record InvoiceLineItem(
    string Description,
    string? Sku,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);

/// <summary>
/// One revision of the invoice produced when a ticket is resolved.
///
/// INVOICES ARE IMMUTABLE ONCE RENDERED. A new edit produces a new <see cref="Revision"/>
/// and the previous row is moved to <see cref="TicketInvoiceStatus.Superseded"/>. This
/// matters because the document has been mailed to a customer and possibly filed for
/// accounting: silently rewriting the numbers on a sent invoice is not acceptable, so
/// there is no update path on a <see cref="TicketInvoiceStatus.Generated"/> row at all.
///
/// Content is snapshotted rather than read live from the order at render time. Catalog
/// prices, order edits and business-name changes all happen; a document that was already
/// sent must still say what it said when it was sent.
///
/// RENDERING IS ASYNCHRONOUS. Queueing a revision only freezes the content and sets
/// <see cref="Status"/> to <see cref="TicketInvoiceStatus.Pending"/>; the PDF is produced by
/// a background worker so a slow renderer never blocks the admin's resolve action.
///
/// PDF bytes live in the database (<see cref="PdfContent"/>) rather than in media storage. An
/// invoice is a few kilobytes, has to be retrievable indefinitely, and must not depend on a
/// third-party CDN still holding the asset.
/// </summary>
public sealed class TicketInvoice : AuditableEntity
{
    public const int NumberMaxLength = 64;
    public const int NotesMaxLength = 2000;
    public const int TermsMaxLength = 4000;
    public const int FooterMaxLength = 1000;
    public const int PartyMaxLength = 300;

    /// <summary>Rendering attempts before the revision is parked in Failed for manual retry.</summary>
    public const int MaxGenerationAttempts = 3;

    private TicketInvoice() { } // EF constructor

    /// <summary>
    /// Queues a new revision for a ticket. Only legal while the invoice is still Pending —
    /// the admin's "edit before generation" window. Once rendering starts the content is
    /// frozen and further edits must create a new revision instead.
    /// </summary>
    public static TicketInvoice Create(
        Guid ticketId,
        string invoiceNumber,
        int revision,
        InvoiceContent content,
        Guid? templateId = null,
        Guid? createdByAdminId = null,
        DateTime? now = null)
    {
        if (ticketId == Guid.Empty)
            throw new ArgumentException("Ticket id is required.", nameof(ticketId));
        if (string.IsNullOrWhiteSpace(invoiceNumber))
            throw new ArgumentException("Invoice number is required.", nameof(invoiceNumber));
        ArgumentNullException.ThrowIfNull(content);
        if (revision < 1)
            throw new ArgumentOutOfRangeException(nameof(revision), "Revision must be 1 or greater.");

        return new TicketInvoice
        {
            TicketId = ticketId,
            InvoiceNumber = invoiceNumber.Trim()[..Math.Min(NumberMaxLength, invoiceNumber.Trim().Length)],
            Revision = revision,
            TemplateId = templateId,
            Content = content,
            Status = TicketInvoiceStatus.Pending,
            GenerationAttempts = 0,
            IsContentOverridden = false,
            CreatedByAdminId = createdByAdminId,
            QueuedAtUtc = now ?? DateTime.UtcNow
        };
    }

    public Guid TicketId { get; private set; }

    /// <summary>External reference, e.g. <c>INV-2026-000042</c>. Unique across revisions.</summary>
    public string InvoiceNumber { get; private set; } = string.Empty;

    /// <summary>1-based counter per ticket. Gaps are expected - a failed revision still consumed one.</summary>
    public int Revision { get; private set; }

    public TicketInvoiceStatus Status { get; private set; }

    /// <summary>The template revision applied at queue time. Null when rendering the built-in default.</summary>
    public Guid? TemplateId { get; private set; }

    /// <summary>Frozen render input. See the type remarks for why this is a snapshot.</summary>
    public InvoiceContent Content { get; private set; } = null!;

    public DateTime QueuedAtUtc { get; private set; }
    public DateTime? GeneratedAtUtc { get; private set; }
    public int GenerationAttempts { get; private set; }

    /// <summary>Rendered PDF. Null until <see cref="Status"/> is Generated.</summary>
    public byte[]? PdfContent { get; private set; }

    public string FileName { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }

    /// <summary>SHA-256 of the PDF, for tamper-evidence and client-side de-duplication.</summary>
    public string? Checksum { get; private set; }

    public string? FailureReason { get; private set; }

    /// <summary>True when an administrator edited the rendered content instead of accepting the default.</summary>
    public bool IsContentOverridden { get; private set; }

    public Guid? CreatedByAdminId { get; private set; }
    public Guid? OverriddenByAdminId { get; private set; }
    public DateTime? OverriddenAtUtc { get; private set; }

    /// <summary>Set once the document has been emailed to the customer. Prevents duplicate sends.</summary>
    public bool EmailedToCustomer { get; private set; }
    public DateTime? EmailedAtUtc { get; private set; }

    // Navigation
    public Ticket Ticket { get; private set; } = null!;

    // -----------------------------------------------------------------------
    // Capability
    // -----------------------------------------------------------------------

    /// <summary>True while the content can still be edited, i.e. rendering has not begun.</summary>
    public bool CanEditContent => Status == TicketInvoiceStatus.Pending;

    /// <summary>True when another automatic rendering attempt should be made.</summary>
    public bool CanRetry =>
        Status == TicketInvoiceStatus.Failed && GenerationAttempts < MaxGenerationAttempts;

    public bool IsTerminal => Status is TicketInvoiceStatus.Generated or TicketInvoiceStatus.Superseded;

    // -----------------------------------------------------------------------
    // Behaviour
    // -----------------------------------------------------------------------

    /// <summary>
    /// Applies an administrator's override to the frozen content. Only legal while Pending:
    /// once a document has been produced, changing it in place would make the stored PDF
    /// and the stored content disagree.
    /// </summary>
    public void OverrideContent(InvoiceContent content, Guid adminId, DateTime? now = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!CanEditContent)
            throw new InvalidOperationException(
                $"Invoice {InvoiceNumber} is {Status}; its content can no longer be edited. " +
                "Queue a new revision instead.");

        Content = content;
        IsContentOverridden = true;
        OverriddenByAdminId = adminId;
        OverriddenAtUtc = now ?? DateTime.UtcNow;
    }

    /// <summary>Records that rendering is being attempted. Returns false once attempts are exhausted.</summary>
    public bool TryBeginGeneration()
    {
        if (Status != TicketInvoiceStatus.Pending && Status != TicketInvoiceStatus.Failed)
            return false;
        if (GenerationAttempts >= MaxGenerationAttempts)
            return false;

        GenerationAttempts++;
        FailureReason = null;
        return true;
    }

    /// <summary>Stores the rendered document and marks the revision complete.</summary>
    public void MarkGenerated(
        byte[] pdf,
        string fileName,
        string checksum,
        DateTime? now = null)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        if (pdf.Length == 0)
            throw new ArgumentException("Rendered PDF is empty.", nameof(pdf));

        PdfContent = pdf;
        FileName = fileName;
        SizeBytes = pdf.LongLength;
        Checksum = checksum;
        Status = TicketInvoiceStatus.Generated;
        GeneratedAtUtc = now ?? DateTime.UtcNow;
        FailureReason = null;
    }

    /// <summary>Records a rendering failure. The worker retries while attempts remain.</summary>
    public void MarkFailed(string reason)
    {
        FailureReason = reason.Length > 2000 ? reason[..2000] : reason;
        Status = TicketInvoiceStatus.Failed;
    }

    /// <summary>Re-queues a failed revision for another attempt.</summary>
    public void Requeue()
    {
        if (Status != TicketInvoiceStatus.Failed)
            throw new InvalidOperationException($"Invoice {InvoiceNumber} is {Status}; only a failed revision can be requeued.");

        Status = TicketInvoiceStatus.Pending;
        FailureReason = null;
    }

    /// <summary>Marks this revision as replaced by a newer one. Only a generated document can be superseded.</summary>
    public void MarkSuperseded()
    {
        if (Status != TicketInvoiceStatus.Generated)
            throw new InvalidOperationException(
                $"Invoice {InvoiceNumber} is {Status}; only a generated revision can be superseded.");

        Status = TicketInvoiceStatus.Superseded;
    }

    /// <summary>Records that the document was mailed. Idempotent by guard so a retried send cannot double-mail.</summary>
    public void MarkEmailed(DateTime? now = null)
    {
        if (EmailedToCustomer) return;
        EmailedToCustomer = true;
        EmailedAtUtc = now ?? DateTime.UtcNow;
    }
}