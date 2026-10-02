using Asp.Versioning;
using KromicCommerce.Application.Features.Catalog.Reviews;
using KromicCommerce.Contracts.Catalog;
using KromicCommerce.Contracts.Common;
using KromicCommerce.Domain.Catalog;
using Microsoft.AspNetCore.Authorization;

namespace KromicCommerce.Api.Controllers.V1;

/// <summary>
/// Admin moderation queue for product reviews.
///
/// Separate from the customer review routes because the trust boundary differs: an admin acts
/// on any customer's review, a customer only on their own. Moderation has no integration or
/// credential dimension, so it needs no settings endpoint — status is the only lever.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/reviews")]
[Authorize(Policy = "AdminOnly")]
public sealed class AdminReviewsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<AdminReviewResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] Guid? productId,
        [FromQuery] int? rating,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var parsed = ParseStatus(status);
        if (!parsed.IsValid)
            return BadRequest();

        var result = await mediator.Send(
            new GetAdminReviewsQuery(parsed.Value, productId, rating, search, page, pageSize), ct);

        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    [HttpGet("{reviewId:guid}")]
    [ProducesResponseType(typeof(AdminReviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid reviewId, CancellationToken ct)
    {
        var result = await mediator.Send(new GetAdminReviewQuery(reviewId), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    /// <summary>
    /// Publishes, rejects, or returns a review to Pending. Rejecting requires a reason, which is
    /// shown back to the author so the decision is explainable.
    /// </summary>
    [HttpPut("{reviewId:guid}/status")]
    [ProducesResponseType(typeof(AdminReviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Moderate(
        Guid reviewId, [FromBody] ModerateReviewRequest request, CancellationToken ct)
    {
        if (request is null) return BadRequest();

        if (!Enum.TryParse<ReviewStatus>(request.Status, ignoreCase: true, out var parsed))
            return BadRequest();

        var result = await mediator.Send(
            new ModerateReviewCommand(reviewId, parsed, request.Reason), ct);

        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    [HttpDelete("{reviewId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid reviewId, CancellationToken ct)
    {
        var result = await mediator.Send(new AdminDeleteReviewCommand(reviewId), ct);
        return result.IsSuccess ? NoContent() : result.Error.ToActionResult();
    }

    /// <summary>
    /// Parses the optional status filter. An unrecognised value is rejected rather than ignored:
    /// silently dropping it would return the whole queue and look to the caller as though the
    /// filter had worked.
    /// </summary>
    private static (ReviewStatus? Value, bool IsValid) ParseStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return (null, true);

        return Enum.TryParse<ReviewStatus>(status, ignoreCase: true, out var parsed)
            ? (parsed, true)
            : (null, false);
    }
}