using System.ComponentModel.DataAnnotations;

namespace KromicCommerce.Contracts.Support;

// ---------------------------------------------------------------------------
// Requests
// ---------------------------------------------------------------------------

/// <summary>
/// Opens a new support ticket. The author is always taken from the access token — no
/// request field can name somebody else.
/// </summary>
public sealed class CreateTicketRequest
{
    [Required]
    [StringLength(200, MinimumLength = 4)]
    public string Subject { get; set; } = string.Empty;

    [Required]
    [StringLength(8000, MinimumLength = 4)]
    public string Description { get; set; } = string.Empty;

    /// <summary>Optional order this is about. Supplies the invoice line data on resolution.</summary>
    public Guid? OrderId { get; set; }
}

/// <summary>
/// Posts a reply. Omit <see cref="ParentCommentId"/> for a top-level message, or pass a
/// comment id to reply underneath it.
/// </summary>
public sealed class PostTicketCommentRequest
{
    [Required]
    [StringLength(10000, MinimumLength = 1)]
    public string Body { get; set; } = string.Empty;

    public Guid? ParentCommentId { get; set; }

    /// <summary>
    /// Media already uploaded through <c>POST /tickets/media</c>, referenced by the provider
    /// id that endpoint returned. Uploading and posting are separate steps so a comment is
    /// still writable when the media provider is unavailable.
    /// </summary>
    public List<TicketAttachmentUploadRequest> Attachments { get; set; } = [];
}

/// <summary>Client reference to a previously uploaded ticket attachment.</summary>
public sealed class TicketAttachmentUploadRequest
{
    /// <summary>Either <c>Image</c> or <c>Video</c>, as reported by the upload endpoint.</summary>
    [Required]
    [StringLength(16)]
    public string Kind { get; set; } = string.Empty;

    [Required]
    [StringLength(512, MinimumLength = 1)]
    public string PublicId { get; set; } = string.Empty;

    [Required]
    [StringLength(1024, MinimumLength = 1)]
    public string SecureUrl { get; set; } = string.Empty;

    [StringLength(20)]
    public string? Format { get; set; }

    [StringLength(120)]
    public string? ContentType { get; set; }

    public int? Width { get; set; }
    public int? Height { get; set; }
    public int? DurationSeconds { get; set; }

    public long SizeBytes { get; set; }

    [StringLength(500)]
    public string? AltText { get; set; }
}

/// <summary>Admin action: mark the ticket resolved.</summary>
public sealed class ResolveTicketRequest
{
    [StringLength(2000)]
    public string? ResolutionNote { get; set; }

    /// <summary>
    /// Overrides the merchant's "auto-generate invoice on resolve" preference for this one
    /// ticket. Null falls back to the stored setting.
    /// </summary>
    public bool? RequestInvoice { get; set; }
}

/// <summary>User action: confirm a resolved ticket is fixed and close it.</summary>
public sealed class CloseTicketRequest
{
    [StringLength(2000)]
    public string? Note { get; set; }
}

/// <summary>User action: resume a resolved or closed conversation.</summary>
public sealed class ReopenTicketRequest
{
    [StringLength(2000)]
    public string? Reason { get; set; }
}

/// <summary>Admin action: change urgency.</summary>
public sealed class SetTicketPriorityRequest
{
    [Required]
    public string Priority { get; set; } = string.Empty;
}

/// <summary>
/// Administrator override of a queued invoice's wording and presentation. Money fields are
/// deliberately absent — a template edit must never be able to change an amount.
/// </summary>
public sealed class OverrideInvoiceContentRequest
{
    [StringLength(200)]
    public string? IssuerName { get; set; }

    [StringLength(300)]
    public string? BillToName { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }

    [StringLength(4000)]
    public string? Terms { get; set; }

    [StringLength(1000)]
    public string? FooterNote { get; set; }

    /// <summary>Six hex digits, with or without the leading '#'. Invalid values are rejected.</summary>
    [StringLength(7)]
    public string? AccentColor { get; set; }
}

/// <summary>Full invoice template content from the admin editor.</summary>
public sealed class UpdateInvoiceTemplateRequest
{
    [Required]
    [StringLength(120, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [StringLength(300)]
    public string? Description { get; set; }

    [StringLength(200)]
    public string? IssuerNameOverride { get; set; }

    [StringLength(120)]
    public string? TaxId { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }

    [StringLength(4000)]
    public string? Terms { get; set; }

    [StringLength(1000)]
    public string? FooterNote { get; set; }

    /// <summary>Six hex digits, with or without the leading '#'. Invalid values are rejected.</summary>
    [StringLength(7)]
    public string AccentColor { get; set; } = "1A1A1A";

    public bool ShowIssuerIdentity { get; set; } = true;
    public bool ShowLineItemTable { get; set; } = true;
    public bool ShowTerms { get; set; } = true;
    public bool ShowNotes { get; set; } = true;
}

/// <summary>Admin control over the support module's automation switches.</summary>
public sealed class UpdateSupportSettingsRequest
{
    /// <summary>
    /// The global invoice mailing toggle. Turning this off stops the email only; invoices
    /// are still generated and remain downloadable.
    /// </summary>
    public bool? AutomatedInvoiceMailingEnabled { get; set; }

    public bool? AutoGenerateInvoiceOnResolve { get; set; }

    /// <summary>Send as null to restore the default subject line.</summary>
    [StringLength(200)]
    public string? InvoiceMailSubjectOverride { get; set; }

    /// <summary>Hours of customer silence before a resolved ticket is auto-closed. Clamped 1..720.</summary>
    [Range(1, 720)]
    public int? AutoCloseIdleHours { get; set; }

    public bool? NotifyAdminOnTicketCreated { get; set; }
    public bool? NotifyAdminOnTicketReopened { get; set; }
    public bool? NotifyCustomerOnTicketResolved { get; set; }

    [Range(0, 6)]
    public int? MaxAttachmentsPerComment { get; set; }
}

// ---------------------------------------------------------------------------
// Responses
// ---------------------------------------------------------------------------

/// <summary>List-row projection. Deliberately omits the conversation body.</summary>
public sealed record TicketSummaryResponse(
    Guid Id,
    string TicketNumber,
    string Subject,
    string Status,
    string Priority,
    Guid CustomerId,
    string CustomerName,
    string CustomerEmail,
    Guid? AssignedAdminId,
    Guid? RelatedOrderId,
    string? OrderNumber,
    int CommentCount,
    int ReopenCount,
    bool AwaitingFirstResponse,
    DateTime CreatedAtUtc,
    DateTime LastActivityAtUtc,
    DateTime? ResolvedAtUtc,
    DateTime? ClosedAtUtc,
    DateTime? AutoCloseAtUtc,
    Guid? LatestInvoiceId,
    string? LatestInvoiceStatus);

/// <summary>Full ticket conversation for the customer view.</summary>
public sealed record TicketDetailResponse(
    Guid Id,
    string TicketNumber,
    string Subject,
    string Description,
    string Status,
    string Priority,
    Guid CustomerId,
    string CustomerName,
    string CustomerEmail,
    Guid? AssignedAdminId,
    Guid? RelatedOrderId,
    string? OrderNumber,
    int ReopenCount,
    bool AwaitingFirstResponse,
    DateTime CreatedAtUtc,
    DateTime LastActivityAtUtc,
    DateTime? ResolvedAtUtc,
    DateTime? ClosedAtUtc,
    DateTime? AutoCloseAtUtc,
    /// <summary>Hierarchy of replies. Internal admin notes are never present here.</summary>
    IReadOnlyList<TicketCommentResponse> Comments,
    IReadOnlyList<TicketStatusHistoryResponse> History,
    IReadOnlyList<TicketInvoiceResponse> Invoices);

/// <summary>Admin view of a ticket. Identical shape to the customer view except for internal notes.</summary>
public sealed record AdminTicketDetailResponse(
    TicketDetailResponse Ticket,
    IReadOnlyList<TicketStatusHistoryResponse> FullHistory);

/// <summary>One node of a ticket's reply tree.</summary>
public sealed record TicketCommentResponse(
    Guid Id,
    Guid? ParentCommentId,
    Guid AuthorId,
    string AuthorName,
    bool IsAdminAuthor,
    string Body,
    int Depth,
    /// <summary>True when this is an administrator's private note, only ever populated for admins.</summary>
    bool IsInternalNote,
    DateTime CreatedAtUtc,
    IReadOnlyList<TicketAttachmentResponse> Attachments,
    IReadOnlyList<TicketCommentResponse> Replies);

/// <summary>An image or video attached to a comment.</summary>
public sealed record TicketAttachmentResponse(
    Guid Id,
    string Kind,
    string PublicId,
    string SecureUrl,
    string? Format,
    string? ContentType,
    int? Width,
    int? Height,
    int? DurationSeconds,
    long SizeBytes,
    string? AltText);

/// <summary>One row of a ticket's state-transition audit log.</summary>
public sealed record TicketStatusHistoryResponse(
    Guid Id,
    string? FromStatus,
    string ToStatus,
    string Actor,
    Guid? ActorId,
    string? ActorName,
    string? Note,
    DateTime OccurredAtUtc);

/// <summary>One invoice revision attached to a ticket.</summary>
public sealed record TicketInvoiceResponse(
    Guid Id,
    string InvoiceNumber,
    int Revision,
    string Status,
    Guid TicketId,
    Guid? TemplateId,
    bool IsContentOverridden,
    DateTime QueuedAtUtc,
    DateTime? GeneratedAtUtc,
    int GenerationAttempts,
    string? FailureReason,
    string FileName,
    long SizeBytes,
    bool EmailedToCustomer,
    DateTime? EmailedAtUtc,
    InvoiceContentResponse Content,
    /// <summary>True when a rendered PDF is available for download.</summary>
    bool HasDocument);

/// <summary>The frozen render input of an invoice revision, as stored.</summary>
public sealed record InvoiceContentResponse(
    string IssuerName,
    string? IssuerAddress,
    string? IssuerEmail,
    string? IssuerTaxId,
    string BillToName,
    string? BillToEmail,
    string? BillToAddress,
    DateTime InvoiceDateUtc,
    string CurrencyCode,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal ShippingAmount,
    decimal CodFee,
    decimal GrandTotal,
    IReadOnlyList<InvoiceLineItemResponse> LineItems,
    string TicketNumber,
    string? TicketSubject,
    string? OrderNumber,
    string? Notes,
    string? Terms,
    string? FooterNote,
    string AccentColor);

/// <summary>One billable line on an invoice.</summary>
public sealed record InvoiceLineItemResponse(
    string Description,
    string? Sku,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);

/// <summary>An invoice layout template.</summary>
public sealed record InvoiceTemplateResponse(
    Guid Id,
    string Name,
    string? Description,
    bool IsDefault,
    string? IssuerNameOverride,
    string? TaxId,
    string? Notes,
    string? Terms,
    string? FooterNote,
    string AccentColor,
    bool ShowIssuerIdentity,
    bool ShowLineItemTable,
    bool ShowTerms,
    bool ShowNotes,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

/// <summary>Current support automation settings, including whether the admin address is configured.</summary>
public sealed record SupportSettingsResponse(
    int AutoCloseIdleHours,
    bool AutomatedInvoiceMailingEnabled,
    bool AutoGenerateInvoiceOnResolve,
    string? InvoiceMailSubjectOverride,
    bool NotifyAdminOnTicketCreated,
    bool NotifyAdminOnTicketReopened,
    bool NotifyCustomerOnTicketResolved,
    int MaxAttachmentsPerComment,
    /// <summary>Countdown surface for the admin dashboard. Never the address itself.</summary>
    bool AdminNotificationConfigured,
    string AdminNotificationTarget);

/// <summary>Result of uploading a media file for a comment, before the comment is posted.</summary>
public sealed record TicketMediaUploadResponse(
    string Kind,
    string PublicId,
    string SecureUrl,
    string? Format,
    string? ContentType,
    int? Width,
    int? Height,
    int? DurationSeconds,
    long SizeBytes);