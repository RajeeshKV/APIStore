using Asp.Versioning;
using KromicCommerce.Application.Abstractions.Auth;
using KromicCommerce.Application.Features.Support;
using KromicCommerce.Contracts.Common;
using KromicCommerce.Contracts.Support;
using KromicCommerce.Domain.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KromicCommerce.Api.Controllers.V1;

/// <summary>
/// Administrative support desk.
///
/// Split from the customer routes because the trust boundary differs: an administrator can
/// author internal notes, see every conversation, resolve (which is what triggers invoice
/// generation) and edit invoices. Only the routes that accept an actor id read it from the
/// token; no body field can name a different administrator.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/tickets")]
[Authorize(Policy = "AdminOnly")]
public sealed class AdminTicketsController(
    IMediator mediator, ICurrentUserService currentUser) : ControllerBase
{
    private Guid? ActorId => currentUser.UserId;

    // -----------------------------------------------------------------------
    // Queue
    // -----------------------------------------------------------------------

    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<TicketSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTickets(
        [FromQuery] string? status,
        [FromQuery] string? priority,
        [FromQuery] string? search,
        [FromQuery] bool? unansweredOnly,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!TryParseEnum<TicketStatus>(status, out var parsedStatus))
            return Invalid("INVALID_TICKET_STATUS", $"'{status}' is not a valid ticket status.");

        if (!TryParseEnum<TicketPriority>(priority, out var parsedPriority))
            return Invalid("INVALID_TICKET_PRIORITY", $"'{priority}' is not a valid priority.");

        var result = await mediator.Send(new GetAdminTicketsQuery(
            parsedStatus, parsedPriority, search, unansweredOnly, page, pageSize), ct);

        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    /// <summary>
    /// Full conversation including internal notes. The note filter is applied by the mapper,
    /// not by this controller, so an admin path can never accidentally serve a customer's view.
    /// </summary>
    [HttpGet("{ticketId:guid}")]
    [ProducesResponseType(typeof(AdminTicketDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTicket(Guid ticketId, CancellationToken ct)
    {
        if (ActorId is null) return Unauthorized();

        var result = await mediator.Send(new GetTicketQuery(ticketId, ActorId, IsAdmin: true), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    // -----------------------------------------------------------------------
    // Reply
    // -----------------------------------------------------------------------

    /// <summary>
    /// Administrator reply. <c>isInternalNote</c> keeps the message out of the customer's
    /// view; only an administrator can set it, and the validator enforces that as well as the
    /// route policy so a UI toggle cannot be the only guard.
    /// </summary>
    [HttpPost("{ticketId:guid}/comments")]
    [ProducesResponseType(typeof(TicketCommentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PostComment(
        Guid ticketId,
        [FromBody] PostTicketCommentRequest request,
        [FromQuery] bool isInternalNote = false,
        CancellationToken ct = default)
    {
        if (ActorId is null) return Unauthorized();
        if (request is null) return BadRequest();

        if (!TicketsController.TryMapAttachments(request.Attachments, out var attachments))
            return BadRequest(new
            {
                error = new
                {
                    code = "INVALID_ATTACHMENT",
                    message = "Attachment kind must be 'Image' or 'Video'."
                }
            });

        var result = await mediator.Send(new AddTicketCommentCommand(
            ticketId,
            ActorId.Value,
            IsAdmin: true,
            request.Body,
            request.ParentCommentId,
            isInternalNote,
            attachments), ct);

        if (!result.IsSuccess) return result.Error.ToActionResult();

        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    // -----------------------------------------------------------------------
    // Transitions
    // -----------------------------------------------------------------------

    /// <summary>
    /// Marks the ticket resolved. This is the only path that produces an invoice, which is why
    /// it is administrator-only.
    /// </summary>
    [HttpPost("{ticketId:guid}/resolve")]
    [ProducesResponseType(typeof(TicketSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Resolve(
        Guid ticketId, [FromBody] ResolveTicketRequest? request, CancellationToken ct)
    {
        if (ActorId is null) return Unauthorized();

        var result = await mediator.Send(new ResolveTicketCommand(
            ticketId,
            ActorId.Value,
            request?.ResolutionNote,
            request?.RequestInvoice,
            InvoiceTemplateId: null), ct);

        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    [HttpPost("{ticketId:guid}/priority")]
    [ProducesResponseType(typeof(TicketSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetPriority(
        Guid ticketId, [FromBody] SetTicketPriorityRequest request, CancellationToken ct)
    {
        if (ActorId is null) return Unauthorized();
        if (request is null) return BadRequest();

        if (!TryParseEnum<TicketPriority>(request.Priority, out var priority))
            return Invalid("INVALID_TICKET_PRIORITY", $"'{request.Priority}' is not a valid priority.");

        var result = await mediator.Send(
            new SetTicketPriorityCommand(ticketId, priority!.Value), ct);

        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    [HttpPost("{ticketId:guid}/assign")]
    [ProducesResponseType(typeof(TicketSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Assign(Guid ticketId, CancellationToken ct)
    {
        if (ActorId is null) return Unauthorized();

        var result = await mediator.Send(new AssignTicketCommand(ticketId, ActorId.Value), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Blank means "no filter"; anything else must name a real value. A typo is rejected
    /// rather than silently ignored, because an ignored filter on an admin queue looks
    /// identical to an empty queue.
    /// </summary>
    private static bool TryParseEnum<T>(string? raw, out T? value) where T : struct, Enum
    {
        value = null;
        if (string.IsNullOrWhiteSpace(raw)) return true;

        if (!Enum.TryParse<T>(raw, ignoreCase: true, out var parsed)) return false;

        value = parsed;
        return true;
    }

    private IActionResult Invalid(string code, string message)
        => BadRequest(new { error = new { code, message } });
}