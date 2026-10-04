using Asp.Versioning;
using KromicCommerce.Application.Abstractions.Auth;
using KromicCommerce.Application.Features.Support;
using KromicCommerce.Contracts.Common;
using KromicCommerce.Contracts.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KromicCommerce.Api.Controllers.V1;

/// <summary>
/// Administrative control over invoice documents and support automation.
///
/// Split by concern rather than folded into the ticket controller: these routes operate on
/// documents and global switches that outlive any single ticket, and mixing them would give
/// the ticket controller a second reason to be changed.
///
/// Money fields are absent from every write path here by design. Amounts are frozen into the
/// invoice at resolution from the order; a template or override edit can change wording and
/// presentation but must never be able to alter a figure.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/support")]
[Authorize(Policy = "AdminOnly")]
public sealed class AdminSupportController(
    IMediator mediator, ICurrentUserService currentUser) : ControllerBase
{
    private Guid? ActorId => currentUser.UserId;

    // -----------------------------------------------------------------------
    // Invoice documents
    // -----------------------------------------------------------------------

    [HttpGet("invoices/{invoiceId:guid}/download")]
    [Produces("application/pdf")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DownloadInvoice(Guid invoiceId, CancellationToken ct)
    {
        if (ActorId is null) return Unauthorized();

        var result = await mediator.Send(
            new GetInvoiceDocumentQuery(invoiceId, ActorId, IsAdmin: true), ct);

        if (result.IsFailure) return result.Error.ToActionResult();

        var document = result.Value;
        return File(document.Content, "application/pdf", document.FileName);
    }

    /// <summary>
    /// Edits the wording of a queued invoice before the renderer picks it up. Only queued
    /// revisions are editable: once a document has been generated and mailed, changing the
    /// stored content would make the archived document disagree with what the customer holds.
    /// </summary>
    [HttpPut("invoices/{invoiceId:guid}/content")]
    [ProducesResponseType(typeof(TicketInvoiceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> OverrideInvoiceContent(
        Guid invoiceId, [FromBody] OverrideInvoiceContentRequest request, CancellationToken ct)
    {
        if (ActorId is null) return Unauthorized();
        if (request is null) return BadRequest();

        var result = await mediator.Send(new OverrideInvoiceContentCommand(
            invoiceId,
            ActorId.Value,
            request.IssuerName,
            request.BillToName,
            request.Notes,
            request.Terms,
            request.FooterNote,
            request.AccentColor), ct);

        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    /// <summary>
    /// Puts a failed revision back in the render queue. Re-enqueues rather than resets the
    /// attempt counter so a revision that has exhausted its attempts cannot loop forever.
    /// </summary>
    [HttpPost("invoices/{invoiceId:guid}/retry")]
    [ProducesResponseType(typeof(TicketInvoiceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RetryInvoice(Guid invoiceId, CancellationToken ct)
    {
        if (ActorId is null) return Unauthorized();

        var result = await mediator.Send(new RetryInvoiceCommand(invoiceId, ActorId.Value), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    // -----------------------------------------------------------------------
    // Templates
    // -----------------------------------------------------------------------

    [HttpGet("invoice-templates")]
    [ProducesResponseType(typeof(IReadOnlyList<InvoiceTemplateResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTemplates(CancellationToken ct)
    {
        var result = await mediator.Send(new GetInvoiceTemplatesQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    [HttpGet("invoice-templates/{templateId:guid}")]
    [ProducesResponseType(typeof(InvoiceTemplateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTemplate(Guid templateId, CancellationToken ct)
    {
        var result = await mediator.Send(new GetInvoiceTemplateQuery(templateId), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    [HttpPut("invoice-templates/{templateId:guid}")]
    [ProducesResponseType(typeof(InvoiceTemplateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateTemplate(
        Guid templateId, [FromBody] UpdateInvoiceTemplateRequest request, CancellationToken ct)
    {
        if (ActorId is null) return Unauthorized();
        if (request is null) return BadRequest();

        var result = await mediator.Send(new UpdateInvoiceTemplateCommand(
            templateId,
            ActorId.Value,
            request.Name,
            request.Description,
            request.IssuerNameOverride,
            request.TaxId,
            request.Notes,
            request.Terms,
            request.FooterNote,
            request.AccentColor,
            request.ShowIssuerIdentity,
            request.ShowLineItemTable,
            request.ShowTerms,
            request.ShowNotes), ct);

        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    // -----------------------------------------------------------------------
    // Automation switches
    // -----------------------------------------------------------------------

    /// <summary>
    /// Current support automation settings. The administrative notification address is
    /// reported only as "is one configured" plus a masked form — it is a deployment-level
    /// secret-ish value, not a merchant preference, and is not editable here.
    /// </summary>
    [HttpGet("settings")]
    [ProducesResponseType(typeof(SupportSettingsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSettings(CancellationToken ct)
    {
        var result = await mediator.Send(new GetSupportSettingsQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    /// <summary>
    /// Flips the automation switches. Every field is optional and null means "leave as is",
    /// so a UI that edits one toggle cannot accidentally reset the others.
    /// </summary>
    [HttpPut("settings")]
    [ProducesResponseType(typeof(SupportSettingsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateSettings(
        [FromBody] UpdateSupportSettingsRequest request, CancellationToken ct)
    {
        if (ActorId is null) return Unauthorized();
        if (request is null) return BadRequest();

        var result = await mediator.Send(new UpdateSupportSettingsCommand(
            ActorId.Value,
            request.AutomatedInvoiceMailingEnabled,
            request.AutoGenerateInvoiceOnResolve,
            request.InvoiceMailSubjectOverride,
            request.AutoCloseIdleHours,
            request.NotifyAdminOnTicketCreated,
            request.NotifyAdminOnTicketReopened,
            request.NotifyCustomerOnTicketResolved,
            request.MaxAttachmentsPerComment), ct);

        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }
}