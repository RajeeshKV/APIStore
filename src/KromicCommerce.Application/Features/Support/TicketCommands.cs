namespace KromicCommerce.Application.Features.Support;

// -------------------------------------------------------------------------
// Customer commands
//
// Every actor id below comes from ICurrentUserService at the controller. No request DTO
// carries a user id, so a caller cannot post, close or reopen a ticket as somebody else.
// IsAdmin is likewise derived from the role claim, never from the request body — it decides
// who authored a comment and which transitions are legal.
// -------------------------------------------------------------------------

/// <summary>Opens a new ticket and notifies the configured administrator.</summary>
public sealed record CreateTicketCommand(
    Guid CustomerId,
    string Subject,
    string Description,
    Guid? OrderId) : ICommand<TicketSummaryResponse>;

/// <summary>
/// Posts a reply in the thread. Pass <paramref name="ParentCommentId"/> to nest, omit it for
/// a top-level message. A comment on a Closed ticket reopens the conversation rather than
/// arriving on a closed thread nobody is watching.
/// </summary>
public sealed record AddTicketCommentCommand(
    Guid TicketId,
    Guid ActorId,
    bool IsAdmin,
    string Body,
    Guid? ParentCommentId,
    bool IsInternalNote = false,
    IReadOnlyList<TicketAttachmentRequest>? Attachments = null) : ICommand<TicketCommentResponse>;

/// <summary>User action: confirm the fix worked and close a resolved ticket.</summary>
public sealed record CloseTicketCommand(Guid TicketId, Guid CustomerId, string? Note)
    : ICommand<TicketSummaryResponse>;

/// <summary>User action: resume a resolved or closed conversation.</summary>
public sealed record ReopenTicketCommand(Guid TicketId, Guid CustomerId, string? Reason)
    : ICommand<TicketSummaryResponse>;

// -------------------------------------------------------------------------
// Admin commands
// -------------------------------------------------------------------------

/// <summary>
/// Admin action: mark the ticket resolved. This is the only path that produces an invoice,
/// which is why it is admin-only — a customer must not be able to trigger a document.
/// </summary>
public sealed record ResolveTicketCommand(
    Guid TicketId,
    Guid AdminId,
    string? ResolutionNote,
    bool? RequestInvoice,
    Guid? InvoiceTemplateId) : ICommand<TicketSummaryResponse>;

/// <summary>Admin action: change urgency.</summary>
public sealed record SetTicketPriorityCommand(Guid TicketId, TicketPriority Priority)
    : ICommand<TicketSummaryResponse>;

/// <summary>Admin action: take ownership of the conversation.</summary>
public sealed record AssignTicketCommand(Guid TicketId, Guid AdminId)
    : ICommand<TicketSummaryResponse>;

/// <summary>Admin action: edit the queued invoice before the renderer picks it up.</summary>
public sealed record OverrideInvoiceContentCommand(
    Guid InvoiceId,
    Guid AdminId,
    string? IssuerName,
    string? BillToName,
    string? Notes,
    string? Terms,
    string? FooterNote,
    string? AccentColor) : ICommand<TicketInvoiceResponse>;

/// <summary>Admin action: put a failed invoice revision back in the render queue.</summary>
public sealed record RetryInvoiceCommand(Guid InvoiceId, Guid AdminId)
    : ICommand<TicketInvoiceResponse>;

/// <summary>Admin action: edit the standardised invoice template.</summary>
public sealed record UpdateInvoiceTemplateCommand(
    Guid TemplateId,
    Guid AdminId,
    string Name,
    string? Description,
    string? IssuerNameOverride,
    string? TaxId,
    string? Notes,
    string? Terms,
    string? FooterNote,
    string AccentColor,
    bool ShowIssuerIdentity,
    bool ShowLineItemTable,
    bool ShowTerms,
    bool ShowNotes) : ICommand<InvoiceTemplateResponse>;

/// <summary>Admin action: flip the automation switches, including the global invoice mailing toggle.</summary>
public sealed record UpdateSupportSettingsCommand(
    Guid AdminId,
    bool? AutomatedInvoiceMailingEnabled,
    bool? AutoGenerateInvoiceOnResolve,
    string? InvoiceMailSubjectOverride,
    int? AutoCloseIdleHours,
    bool? NotifyAdminOnTicketCreated,
    bool? NotifyAdminOnTicketReopened,
    bool? NotifyCustomerOnTicketResolved,
    int? MaxAttachmentsPerComment) : ICommand<SupportSettingsResponse>;

// -------------------------------------------------------------------------
// Queries
// -------------------------------------------------------------------------

/// <summary>Customer's own tickets. Scoped server-side by CustomerId — never a request parameter.</summary>
public sealed record GetMyTicketsQuery(
    Guid CustomerId,
    TicketStatus? Status,
    int Page,
    int PageSize) : IQuery<PagedResponse<TicketSummaryResponse>>;

/// <summary>System-wide queue. Admin only.</summary>
public sealed record GetAdminTicketsQuery(
    TicketStatus? Status,
    TicketPriority? Priority,
    string? Search,
    bool? UnansweredOnly,
    int Page,
    int PageSize) : IQuery<PagedResponse<TicketSummaryResponse>>;

/// <summary>
/// Full conversation. A non-admin caller only ever sees their own ticket, and never sees an
/// administrator's internal notes.
/// </summary>
public sealed record GetTicketQuery(Guid TicketId, Guid? ActorId, bool IsAdmin)
    : IQuery<AdminTicketDetailResponse>;

public sealed record GetTicketInvoicesQuery(Guid TicketId, Guid? ActorId, bool IsAdmin)
    : IQuery<IReadOnlyList<TicketInvoiceResponse>>;

public sealed record GetInvoiceTemplateQuery(Guid TemplateId) : IQuery<InvoiceTemplateResponse>;

public sealed record GetInvoiceTemplatesQuery() : IQuery<IReadOnlyList<InvoiceTemplateResponse>>;

public sealed record GetSupportSettingsQuery() : IQuery<SupportSettingsResponse>;

/// <summary>
/// Fetches the rendered bytes of one invoice revision for download.
/// Scoped by <paramref name="ActorId"/> in the handler, so a customer can only download from
/// their own ticket.
/// </summary>
public sealed record GetInvoiceDocumentQuery(Guid InvoiceId, Guid? ActorId, bool IsAdmin)
    : IQuery<InvoiceDocumentResponse>;

/// <summary>Raw PDF payload plus the metadata the download needs for headers and logging.</summary>
public sealed record InvoiceDocumentResponse(
    byte[] Content,
    string FileName,
    string InvoiceNumber,
    int Revision);

// -------------------------------------------------------------------------
// Validators
//
// Length and range rules that the domain also enforces are repeated here on purpose: the
// domain guard turns a bypass into a thrown argument exception, the validator turns it into
// a 400 with a useful message. Neither is sufficient alone.
// -------------------------------------------------------------------------

internal sealed class CreateTicketValidator : AbstractValidator<CreateTicketCommand>
{
    public CreateTicketValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Subject).NotEmpty().MaximumLength(Ticket.SubjectMaxLength);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(Ticket.DescriptionMaxLength);

        // An order id of Guid.Empty is what a client sends for "no order" when it binds a
        // missing JSON value, and it would otherwise become a dangling foreign key.
        RuleFor(x => x.OrderId)
            .Must(id => id is null || id.Value != Guid.Empty)
            .WithMessage("Order id must not be an empty guid.");
    }
}

internal sealed class AddTicketCommentValidator : AbstractValidator<AddTicketCommentCommand>
{
    public AddTicketCommentValidator()
    {
        RuleFor(x => x.TicketId).NotEmpty();
        RuleFor(x => x.ActorId).NotEmpty();
        RuleFor(x => x.Body).NotEmpty().MaximumLength(TicketComment.BodyMaxLength);

        RuleFor(x => x.ParentCommentId)
            .Must(id => id is null || id.Value != Guid.Empty)
            .WithMessage("Parent comment id must not be an empty guid.");

        // Only an administrator may write a private note. Enforced here as well as on the
        // entity so a UI that renders the toggle cannot be the only guard.
        RuleFor(x => x.IsInternalNote)
            .Must((cmd, value) => !value || cmd.IsAdmin)
            .WithMessage("Only an administrator can author an internal note.");

        RuleFor(x => x.Attachments)
            .Must(a => a is null || a.Count <= TicketComment.MaxAttachments)
            .WithMessage($"At most {TicketComment.MaxAttachments} attachments per comment.");

        RuleForEach(x => x.Attachments).ChildRules(attachment =>
        {
            attachment.RuleFor(a => a.PublicId).NotEmpty();
            attachment.RuleFor(a => a.SecureUrl).NotEmpty();
            attachment.RuleFor(a => a.SizeBytes).GreaterThan(0);
        });
    }
}

internal sealed class CloseTicketValidator : AbstractValidator<CloseTicketCommand>
{
    public CloseTicketValidator()
    {
        RuleFor(x => x.TicketId).NotEmpty();
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(Ticket.NoteMaxLength);
    }
}

internal sealed class ReopenTicketValidator : AbstractValidator<ReopenTicketCommand>
{
    public ReopenTicketValidator()
    {
        RuleFor(x => x.TicketId).NotEmpty();
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Reason).MaximumLength(Ticket.NoteMaxLength);
    }
}

internal sealed class ResolveTicketValidator : AbstractValidator<ResolveTicketCommand>
{
    public ResolveTicketValidator()
    {
        RuleFor(x => x.TicketId).NotEmpty();
        RuleFor(x => x.AdminId).NotEmpty();
        RuleFor(x => x.ResolutionNote).MaximumLength(Ticket.NoteMaxLength);
        RuleFor(x => x.InvoiceTemplateId)
            .Must(id => id is null || id.Value != Guid.Empty)
            .WithMessage("Invoice template id must not be an empty guid.");
    }
}

internal sealed class SetTicketPriorityValidator : AbstractValidator<SetTicketPriorityCommand>
{
    public SetTicketPriorityValidator()
    {
        RuleFor(x => x.TicketId).NotEmpty();
        RuleFor(x => x.Priority).IsInEnum();
    }
}

internal sealed class OverrideInvoiceContentValidator : AbstractValidator<OverrideInvoiceContentCommand>
{
    public OverrideInvoiceContentValidator()
    {
        RuleFor(x => x.InvoiceId).NotEmpty();
        RuleFor(x => x.AdminId).NotEmpty();

        RuleFor(x => x.IssuerName).MaximumLength(200);
        RuleFor(x => x.BillToName).MaximumLength(InvoiceContent.PartyMaxLength);
        RuleFor(x => x.Notes).MaximumLength(TicketInvoice.NotesMaxLength);
        RuleFor(x => x.Terms).MaximumLength(TicketInvoice.TermsMaxLength);
        RuleFor(x => x.FooterNote).MaximumLength(TicketInvoice.FooterMaxLength);

        // Six hex digits with an optional leading '#'. Checked here so a typo comes back as a
        // 400 with a usable message instead of a silently black document.
        RuleFor(x => x.AccentColor)
            .Must(IsHexColor)
            .When(x => !string.IsNullOrWhiteSpace(x.AccentColor))
            .WithMessage("Accent colour must be six hex digits, e.g. 1A1A1A or #1A1A1A.");

        // An override that changes nothing is almost always a UI bug — it would flag the
        // revision as customised and record a misleading audit trail.
        RuleFor(x => x).Must(cmd =>
            !string.IsNullOrWhiteSpace(cmd.IssuerName) ||
            !string.IsNullOrWhiteSpace(cmd.BillToName) ||
            !string.IsNullOrWhiteSpace(cmd.Notes) ||
            !string.IsNullOrWhiteSpace(cmd.Terms) ||
            !string.IsNullOrWhiteSpace(cmd.FooterNote) ||
            !string.IsNullOrWhiteSpace(cmd.AccentColor))
            .WithMessage("At least one invoice field must be supplied.");
    }

    internal static bool IsHexColor(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith('#')) trimmed = trimmed[1..];
        return trimmed.Length == 6 && trimmed.All(Uri.IsHexDigit);
    }
}

internal sealed class UpdateInvoiceTemplateValidator : AbstractValidator<UpdateInvoiceTemplateCommand>
{
    public UpdateInvoiceTemplateValidator()
    {
        RuleFor(x => x.TemplateId).NotEmpty();
        RuleFor(x => x.AdminId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(InvoiceTemplate.NameMaxLength);
        RuleFor(x => x.Description).MaximumLength(InvoiceTemplate.DescriptionMaxLength);
        RuleFor(x => x.IssuerNameOverride).MaximumLength(InvoiceTemplate.IssuerNameMaxLength);
        RuleFor(x => x.TaxId).MaximumLength(InvoiceTemplate.TaxIdMaxLength);
        RuleFor(x => x.Notes).MaximumLength(InvoiceTemplate.NotesMaxLength);
        RuleFor(x => x.Terms).MaximumLength(InvoiceTemplate.TermsMaxLength);
        RuleFor(x => x.FooterNote).MaximumLength(InvoiceTemplate.FooterMaxLength);

        RuleFor(x => x.AccentColor)
            .Must(OverrideInvoiceContentValidator.IsHexColor)
            .WithMessage("Accent colour must be six hex digits, e.g. 1A1A1A or #1A1A1A.");
    }
}

internal sealed class UpdateSupportSettingsValidator : AbstractValidator<UpdateSupportSettingsCommand>
{
    public UpdateSupportSettingsValidator()
    {
        RuleFor(x => x.AdminId).NotEmpty();
        // && not ||: with || the predicate is "at least 1 hour OR at most 720 hours", which
        // every integer satisfies, so the range check silently accepted anything.
        RuleFor(x => x.AutoCloseIdleHours)
            .Must(h => h is null
                || (h.Value >= SupportSettings.MinAutoCloseIdleHours
                    && h.Value <= SupportSettings.MaxAutoCloseIdleHours))
            .WithMessage(
                $"Idle window must be between {SupportSettings.MinAutoCloseIdleHours} and " +
                $"{SupportSettings.MaxAutoCloseIdleHours} hours.")
            .WithErrorCode("IdleWindowOutOfRange");

        RuleFor(x => x.MaxAttachmentsPerComment)
            .Must(m => m is null || m.Value >= 0 && m.Value <= TicketComment.MaxAttachments)
            .WithMessage($"Attachment limit cannot exceed {TicketComment.MaxAttachments}.");

        RuleFor(x => x.InvoiceMailSubjectOverride).MaximumLength(200);
    }
}

internal sealed class GetMyTicketsValidator : AbstractValidator<GetMyTicketsQuery>
{
    public GetMyTicketsValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).GreaterThanOrEqualTo(1).LessThanOrEqualTo(50);
        RuleFor(x => x.Status).IsInEnum().When(x => x.Status.HasValue);
    }
}

internal sealed class GetAdminTicketsValidator : AbstractValidator<GetAdminTicketsQuery>
{
    public GetAdminTicketsValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).GreaterThanOrEqualTo(1).LessThanOrEqualTo(100);
        RuleFor(x => x.Status).IsInEnum().When(x => x.Status.HasValue);
        RuleFor(x => x.Priority).IsInEnum().When(x => x.Priority.HasValue);
        RuleFor(x => x.Search).MaximumLength(200);
    }
}

internal sealed class GetTicketValidator : AbstractValidator<GetTicketQuery>
{
    public GetTicketValidator()
    {
        RuleFor(x => x.TicketId).NotEmpty();

        // An unauthenticated read of a specific ticket is not a thing: the controller requires
        // authentication, and the handler needs an actor to scope the query to.
        RuleFor(x => x.ActorId)
            .NotNull()
            .WithMessage("An authenticated actor is required to read a ticket.");
    }
}

internal sealed class GetInvoiceDocumentValidator : AbstractValidator<GetInvoiceDocumentQuery>
{
    public GetInvoiceDocumentValidator()
    {
        RuleFor(x => x.InvoiceId).NotEmpty();

        // Without an actor there is nothing to scope the download to, so the request is
        // refused rather than treated as an admin read.
        RuleFor(x => x.ActorId)
            .NotNull()
            .WithMessage("An authenticated actor is required to download an invoice.");
    }
}