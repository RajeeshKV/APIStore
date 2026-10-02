using Asp.Versioning;
using KromicCommerce.Application.Abstractions.Auth;
using KromicCommerce.Application.Abstractions.Media;
using KromicCommerce.Application.Features.Catalog.Reviews;
using KromicCommerce.Contracts.Catalog;
using KromicCommerce.Contracts.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace KromicCommerce.Api.Controllers.V1;

/// <summary>
/// Customer product reviews.
///
/// The listing route is anonymous — reviews are public content — but every write route is
/// authenticated, and the customer id is always taken from <see cref="ICurrentUserService"/>.
/// No request body carries a customer id, so ownership cannot be forged.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/products/{productId:guid}/reviews")]
public sealed class ProductReviewsController(
    IMediator mediator, ICurrentUserService currentUser) : ControllerBase
{
    private Guid? CustomerId => currentUser.UserId;

    /// <summary>
    /// Public listing of published reviews, with the aggregate and per-star breakdown.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ReviewListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        Guid productId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? sort = null,
        [FromQuery] int? rating = null,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(
            new GetProductReviewsQuery(productId, page, pageSize, sort, rating), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    [HttpPost]
    [Authorize(Policy = "CustomerOrAdmin")]
    [ProducesResponseType(typeof(MyReviewResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        Guid productId, [FromBody] CreateReviewRequest request, CancellationToken ct)
    {
        if (CustomerId is null) return Unauthorized();
        if (request is null) return BadRequest();

        var result = await mediator.Send(new SubmitReviewCommand(
            CustomerId.Value, productId, request.ProductVariantId,
            request.Rating, request.Title, request.Body,
            request.Images ?? []), ct);

        if (!result.IsSuccess) return result.Error.ToActionResult();

        return CreatedAtAction(
            nameof(List),
            new { productId, version = "1" },
            result.Value);
    }
}

/// <summary>
/// Operations on a review the caller owns, addressed by review id rather than by product so a
/// customer can reach their own review from a "my reviews" page.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/reviews")]
[Authorize(Policy = "CustomerOrAdmin")]
public sealed class ReviewsController(
    IMediator mediator,
    ICloudinaryService cloudinary,
    ICurrentUserService currentUser) : ControllerBase
{
    private static readonly string[] AllowedMimeTypes =
        ["image/jpeg", "image/png", "image/webp", "image/avif"];

    private Guid? CustomerId => currentUser.UserId;

    /// <summary>
    /// Uploads a photo to Cloudinary for use in a review.
    ///
    /// This is a customer-scoped Cloudinary write, so it has its own endpoint rather than
    /// reusing an admin upload route: widening <c>/products/{id}/images</c> to customers would
    /// hand the entire catalog media surface to any logged-in account.
    ///
    /// The upload is a two-step flow — upload here, then reference the returned
    /// <c>publicId</c>/<c>url</c> when creating or editing a review. That does allow a customer
    /// to upload without ever submitting a review, leaving an orphaned asset; a cleanup job for
    /// those is a reasonable follow-up but is not worth building before there is usage data.
    ///
    /// Rate limited per account rather than per IP, so one abusive customer cannot exhaust the
    /// quota for everyone behind a shared egress.
    /// </summary>
    [HttpPost("images")]
    [EnableRateLimiting(RateLimitingExtensions.MediaUploadPolicy)]
    [ProducesResponseType(typeof(ReviewImageUploadResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> UploadImage(IFormFile file, CancellationToken ct)
    {
        if (CustomerId is null) return Unauthorized();
        if (file is null || file.Length == 0)
            return BadRequest(new { error = new { code = "EMPTY_FILE", message = "A file is required." } });

        if (!AllowedMimeTypes.Contains(file.ContentType.ToLowerInvariant()))
            return BadRequest(new { error = new { code = "INVALID_MIME_TYPE",
                message = $"Type '{file.ContentType}' is not allowed." } });

        await using var stream = file.OpenReadStream();
        var result = await cloudinary.UploadImageAsync(
            stream, file.FileName, "reviews", null, ct);

        if (!result.Success)
            return StatusCode(StatusCodes.Status502BadGateway,
                new { error = new { code = "UPLOAD_FAILED", message = result.ErrorMessage } });

        return StatusCode(StatusCodes.Status201Created, new ReviewImageUploadResponse(
            result.PublicId!,
            result.SecureUrl!,
            result.Format,
            result.Width,
            result.Height));
    }

    /// <summary>
    /// The caller's own reviews, including Pending and Rejected ones so the UI can explain why
    /// a review is not yet public.
    /// </summary>
    [HttpGet("mine")]
    [ProducesResponseType(typeof(PagedResponse<MyReviewResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Mine(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (CustomerId is null) return Unauthorized();
        var result = await mediator.Send(new GetMyReviewsQuery(CustomerId.Value, page, pageSize), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    [HttpPut("{reviewId:guid}")]
    [ProducesResponseType(typeof(MyReviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid reviewId, [FromBody] UpdateReviewRequest request, CancellationToken ct)
    {
        if (CustomerId is null) return Unauthorized();
        if (request is null) return BadRequest();

        var result = await mediator.Send(new EditReviewCommand(
            CustomerId.Value, reviewId,
            request.Rating, request.Title, request.Body,
            request.Images ?? []), ct);

        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    [HttpDelete("{reviewId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid reviewId, CancellationToken ct)
    {
        if (CustomerId is null) return Unauthorized();
        var result = await mediator.Send(new DeleteReviewCommand(CustomerId.Value, reviewId), ct);
        return result.IsSuccess ? NoContent() : result.Error.ToActionResult();
    }

    /// <summary>
    /// Toggles the caller's "this helped" vote. Returns the resulting state and the new count.
    /// Voting on your own review is rejected.
    /// </summary>
    [HttpPost("{reviewId:guid}/helpful")]
    [ProducesResponseType(typeof(ReviewHelpfulResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ToggleHelpful(Guid reviewId, CancellationToken ct)
    {
        if (CustomerId is null) return Unauthorized();
        var result = await mediator.Send(new ToggleReviewHelpfulCommand(CustomerId.Value, reviewId), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }
}