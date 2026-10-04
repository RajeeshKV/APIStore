using Asp.Versioning;
using KromicCommerce.Application.Abstractions.Auth;
using KromicCommerce.Application.Abstractions.Media;
using KromicCommerce.Application.Features.Support;
using KromicCommerce.Contracts.Common;
using KromicCommerce.Contracts.Support;
using KromicCommerce.Domain.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KromicCommerce.Api.Controllers.V1;

/// <summary>
/// Customer support tickets.
///
/// The acting customer is read from <see cref="ICurrentUserService"/> on every route and is
/// never taken from a body, route or query value — there is no request field that can name
/// another account. Ownership is additionally re-checked inside each handler, so a guessed
/// ticket id returns 404 rather than another customer's conversation.
///
/// Internal administrator notes are filtered out on the read path, not merely hidden by the UI.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/tickets")]
[Authorize(Policy = "CustomerOrAdmin")]
public sealed class TicketsController(
    IMediator mediator,
    ICurrentUserService currentUser,
    ICloudinaryService cloudinary) : ControllerBase
{
    /// <summary>
    /// MIME allow-list. An allow-list rather than a deny-list: a support ticket is a
    /// customer-facing upload path, and a deny-list only ever covers the formats somebody
    /// already thought of.
    /// </summary>
    private static readonly HashSet<string> AllowedImageTypes =
        new(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/png", "image/webp", "image/gif", "image/avif" };

    private static readonly HashSet<string> AllowedVideoTypes =
        new(StringComparer.OrdinalIgnoreCase) { "video/mp4", "video/webm", "video/quicktime" };

    private const long MaxImageBytes = 10L * 1024 * 1024;
    private const long MaxVideoBytes = 50L * 1024 * 1024;
    private const string UploadFolder = "support";

    private Guid? ActorId => currentUser.UserId;

    /// <summary>
    /// An administrator reaching a customer route acts as an administrator, not as a customer.
    /// Taking this from the role claim rather than from the route means a customer can never
    /// author an internal note by hitting an admin endpoint shape with their own token.
    /// </summary>
    private bool IsAdmin =>
        string.Equals(currentUser.Role, "Admin", StringComparison.OrdinalIgnoreCase);

    // -----------------------------------------------------------------------
    // List / read
    // -----------------------------------------------------------------------

    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<TicketSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyTickets(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (ActorId is null) return Unauthorized();

        if (!TryParseStatus(status, out var parsedStatus))
            return Invalid("INVALID_TICKET_STATUS", $"'{status}' is not a valid ticket status.");

        var result = await mediator.Send(
            new GetMyTicketsQuery(ActorId.Value, parsedStatus, page, pageSize), ct);

        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    [HttpGet("{ticketId:guid}")]
    [ProducesResponseType(typeof(AdminTicketDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTicket(Guid ticketId, CancellationToken ct)
    {
        if (ActorId is null) return Unauthorized();

        var result = await mediator.Send(new GetTicketQuery(ticketId, ActorId, IsAdmin), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    [HttpGet("{ticketId:guid}/invoices")]
    [ProducesResponseType(typeof(IReadOnlyList<TicketInvoiceResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetInvoices(Guid ticketId, CancellationToken ct)
    {
        if (ActorId is null) return Unauthorized();

        var result = await mediator.Send(new GetTicketInvoicesQuery(ticketId, ActorId, IsAdmin), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    /// <summary>
    /// Streams a rendered invoice revision. Content comes from the database rather than from
    /// Cloudinary so that an administrator's content override and a regenerated revision are
    /// both reflected, and so a stored document stays available even if the media account is
    /// later changed.
    /// </summary>
    [HttpGet("invoices/{invoiceId:guid}/download")]
    [Produces("application/pdf")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DownloadInvoice(Guid invoiceId, CancellationToken ct)
    {
        if (ActorId is null) return Unauthorized();

        var result = await mediator.Send(new GetInvoiceDocumentQuery(invoiceId, ActorId, IsAdmin), ct);
        if (result.IsFailure) return result.Error.ToActionResult();

        var document = result.Value;
        return File(document.Content, "application/pdf", document.FileName);
    }

    // -----------------------------------------------------------------------
    // Create / reply
    // -----------------------------------------------------------------------

    [HttpPost]
    [ProducesResponseType(typeof(TicketSummaryResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create([FromBody] CreateTicketRequest request, CancellationToken ct)
    {
        if (ActorId is null) return Unauthorized();
        if (request is null) return BadRequest();

        var result = await mediator.Send(new CreateTicketCommand(
            ActorId.Value, request.Subject, request.Description, request.OrderId), ct);

        if (!result.IsSuccess) return result.Error.ToActionResult();

        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    /// <summary>
    /// Posts a reply. <c>ParentCommentId</c> nests the reply under an existing comment;
    /// omitting it posts at the top level.
    /// </summary>
    [HttpPost("{ticketId:guid}/comments")]
    [ProducesResponseType(typeof(TicketCommentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PostComment(
        Guid ticketId, [FromBody] PostTicketCommentRequest request, CancellationToken ct)
    {
        if (ActorId is null) return Unauthorized();
        if (request is null) return BadRequest();

        if (!TryMapAttachments(request.Attachments, out var attachments))
            return Invalid("INVALID_ATTACHMENT", "Attachment kind must be 'Image' or 'Video'.");

        var result = await mediator.Send(new AddTicketCommentCommand(
            ticketId,
            ActorId.Value,
            IsAdmin,
            request.Body,
            request.ParentCommentId,
            IsInternalNote: false,
            attachments), ct);

        if (!result.IsSuccess) return result.Error.ToActionResult();

        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    /// <summary>
    /// Two-step attachment flow. The file is uploaded first and its provider id comes back;
    /// the client then posts the comment carrying that id in <c>attachments</c>. Uploading and
    /// commenting are separate steps on purpose — a comment must still be writable when the
    /// media provider is down, and an orphaned upload costs storage but breaks nothing.
    /// </summary>
    [HttpPost("media")]
    [RequestSizeLimit(MaxVideoBytes)]
    [ProducesResponseType(typeof(TicketMediaUploadResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> UploadMedia(
        IFormFile file,
        [FromForm] string? altText = null,
        CancellationToken ct = default)
    {
        if (ActorId is null) return Unauthorized();
        if (file is null || file.Length == 0)
            return Invalid("EMPTY_FILE", "A non-empty file is required.");

        var contentType = file.ContentType ?? string.Empty;
        var isImage = AllowedImageTypes.Contains(contentType);
        var isVideo = AllowedVideoTypes.Contains(contentType);

        if (!isImage && !isVideo)
        {
            return Invalid(
                "INVALID_MIME_TYPE",
                $"Content type '{contentType}' is not accepted. " +
                "Allowed images: " + string.Join(", ", AllowedImageTypes) +
                ". Allowed videos: " + string.Join(", ", AllowedVideoTypes) + ".");
        }

        var ceiling = isImage ? MaxImageBytes : MaxVideoBytes;
        if (file.Length > ceiling)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new
            {
                error = new
                {
                    code = "FILE_TOO_LARGE",
                    message = $"Maximum size for this content type is {ceiling / (1024 * 1024)} MB."
                }
            });
        }

        // The ticket folder is partitioned by the customer so one account's uploads cannot be
        // enumerated through the media provider's console.
        var folder = $"{UploadFolder}/{ActorId.Value:N}";

        await using var stream = file.OpenReadStream();
        var uploaded = isImage
            ? await cloudinary.UploadImageAsync(stream, file.FileName, folder, altText, ct)
            : await cloudinary.UploadVideoAsync(stream, file.FileName, folder, altText, ct);

        if (!uploaded.Success)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = new { code = "UPLOAD_FAILED", message = uploaded.ErrorMessage ?? "Upload failed." }
            });
        }

        var response = new TicketMediaUploadResponse(
            isImage ? nameof(TicketAttachmentKind.Image) : nameof(TicketAttachmentKind.Video),
            uploaded.PublicId!,
            uploaded.SecureUrl!,
            uploaded.Format,
            contentType,
            uploaded.Width,
            uploaded.Height,
            uploaded.DurationSeconds,
            file.Length);

        return StatusCode(StatusCodes.Status201Created, response);
    }

    // -----------------------------------------------------------------------
    // Lifecycle — customer-owned transitions
    // -----------------------------------------------------------------------

    /// <summary>
    /// Confirms the fix worked. Only valid while the ticket is Resolved — resolving is the
    /// administrator's action, closing is the customer's confirmation.
    /// </summary>
    [HttpPost("{ticketId:guid}/close")]
    [ProducesResponseType(typeof(TicketSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Close(
        Guid ticketId, [FromBody] CloseTicketRequest? request, CancellationToken ct)
    {
        if (ActorId is null) return Unauthorized();

        var result = await mediator.Send(
            new CloseTicketCommand(ticketId, ActorId.Value, request?.Note), ct);

        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    [HttpPost("{ticketId:guid}/reopen")]
    [ProducesResponseType(typeof(TicketSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reopen(
        Guid ticketId, [FromBody] ReopenTicketRequest? request, CancellationToken ct)
    {
        if (ActorId is null) return Unauthorized();

        var result = await mediator.Send(
            new ReopenTicketCommand(ticketId, ActorId.Value, request?.Reason), ct);

        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Absent or blank status means "no filter". Anything else must name a real status, so a
    /// typo returns a 400 instead of silently returning the unfiltered page — which reads as
    /// "your filter worked" and hides the client's bug.
    /// </summary>
    private static bool TryParseStatus(string? raw, out TicketStatus? status)
    {
        status = null;
        if (string.IsNullOrWhiteSpace(raw)) return true;

        return Enum.TryParse<TicketStatus>(raw, ignoreCase: true, out var parsed)
            ? ((status = parsed), true).Item2
            : false;
    }

    /// <summary>
    /// Translates the client's attachment references into domain requests.
    ///
    /// Only the provider id and URL are taken from the request. A caller-supplied id is not
    /// re-verified against Cloudinary: the reference is resolved from the ticket's own media
    /// folder on read, so a forged id yields a dead image rather than someone else's file.
    /// </summary>
    internal static bool TryMapAttachments(
        List<TicketAttachmentUploadRequest>? source,
        out IReadOnlyList<TicketAttachmentRequest> attachments)
    {
        attachments = [];

        if (source is null || source.Count == 0) return true;

        var mapped = new List<TicketAttachmentRequest>(source.Count);

        foreach (var item in source)
        {
            if (!Enum.TryParse<TicketAttachmentKind>(item.Kind, ignoreCase: true, out var kind))
                return false;

            // Image and Video are the only kinds the module stores. Anything else — including
            // a numeric enum value that happens to parse — would be persisted as an unrenderable
            // attachment kind.
            if (kind is not (TicketAttachmentKind.Image or TicketAttachmentKind.Video))
                return false;

            mapped.Add(new TicketAttachmentRequest(
                kind,
                item.PublicId,
                item.SecureUrl,
                item.Format,
                item.ContentType,
                item.Width,
                item.Height,
                item.DurationSeconds,
                item.SizeBytes,
                item.AltText));
        }

        attachments = mapped;
        return true;
    }

    private IActionResult Invalid(string code, string message)
        => BadRequest(new { error = new { code, message } });
}