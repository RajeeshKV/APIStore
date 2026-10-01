using Asp.Versioning;
using KromicCommerce.Application.Abstractions.Media;
using KromicCommerce.Application.Features.Admin.Carousel;
using KromicCommerce.Contracts.Catalog;
using Microsoft.AspNetCore.Authorization;

namespace KromicCommerce.Api.Controllers.V1;

/// <summary>
/// Storefront Home page carousel.
/// </summary>
/// <remarks>
/// Mixed public/admin controller, following <c>CategoriesController</c>: no class-level
/// <c>[Authorize]</c>, so the storefront read is anonymous by omission and every admin action
/// opts in with the <c>AdminOnly</c> policy. An endpoint in this codebase is anonymous simply by
/// having no <c>[Authorize]</c> attribute.
/// <para>
/// The carousel CTA always points at the Shop route. There is no per-slide URL anywhere in this
/// contract, so a slide cannot be turned into an arbitrary link.
/// </para>
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/carousel")]
public sealed class CarouselController(IMediator mediator, ICloudinaryService cloudinary)
    : ControllerBase
{
    private static readonly string[] AllowedMimeTypes =
        ["image/jpeg", "image/png", "image/webp", "image/gif", "image/avif"];

    /// <summary>Maximum slide image size: 10 MB.</summary>
    private const long MaxImageBytes = 10 * 1024 * 1024;

    // -----------------------------------------------------------------------
    // Public storefront
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns the Home page carousel for anonymous storefront visitors.
    /// Only active slides that have an image are returned, ordered by display order then creation
    /// time. Returns an empty array when nothing is configured — never a 404.
    /// </summary>
    [HttpGet("/api/v{version:apiVersion}/store/carousel")]
    [ProducesResponseType(typeof(IReadOnlyList<StorefrontCarouselSlideResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStorefrontCarousel(CancellationToken ct)
    {
        var result = await mediator.Send(new GetStorefrontCarouselQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    // -----------------------------------------------------------------------
    // Admin — read
    // -----------------------------------------------------------------------

    /// <summary>
    /// Lists carousel slides for the admin screen, including inactive ones.
    /// Pass <paramref name="activeOnly"/> to filter. Ordered by display order then creation time.
    /// </summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CarouselSlideResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll([FromQuery] bool activeOnly = false, CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetAdminCarouselSlidesQuery(activeOnly), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    /// <summary>Gets one carousel slide, including inactive ones.</summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CarouselSlideResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await mediator.Send(new GetAdminCarouselSlideQuery(id), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    // -----------------------------------------------------------------------
    // Admin — write
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates a carousel slide. The image is attached afterwards with
    /// <c>PUT /api/v1/admin/carousel/{id}/image</c>; a slide with no image is never returned by
    /// the public carousel endpoint.
    /// </summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpPost]
    [ProducesResponseType(typeof(CarouselSlideResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateCarouselSlideRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(
            new CreateCarouselSlideCommand(
                request.Title, request.Subtitle, request.CtaText, request.SortOrder, request.IsActive),
            ct);

        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Value.Id, version = "1" }, result.Value)
            : result.Error.ToActionResult();
    }

    /// <summary>Updates slide text, CTA label, display order and active status.</summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(CarouselSlideResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateCarouselSlideRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(
            new UpdateCarouselSlideCommand(
                id, request.Title, request.Subtitle, request.CtaText, request.SortOrder, request.IsActive),
            ct);

        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    /// <summary>Deletes a carousel slide.</summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await mediator.Send(new DeleteCarouselSlideCommand(id), ct);
        return result.IsSuccess ? NoContent() : result.Error.ToActionResult();
    }

    // -----------------------------------------------------------------------
    // Admin — image
    // -----------------------------------------------------------------------

    /// <summary>
    /// Uploads or replaces the slide image to Cloudinary and stores the returned reference.
    /// Accepted types: JPEG, PNG, WebP, GIF, AVIF. Maximum 10 MB.
    /// </summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpPut("{id:guid}/image")]
    [ProducesResponseType(typeof(CarouselSlideResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> UploadImage(Guid id, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = new { code = "NO_FILE", message = "No file provided." } });

        if (!AllowedMimeTypes.Contains(file.ContentType.ToLowerInvariant()))
            return BadRequest(new { error = new { code = "INVALID_MIME_TYPE",
                message = $"File type '{file.ContentType}' is not allowed." } });

        if (file.Length > MaxImageBytes)
            return BadRequest(new { error = new { code = "FILE_TOO_LARGE",
                message = "Image must be 10 MB or smaller." } });

        await using var stream = file.OpenReadStream();
        var uploadResult = await cloudinary.UploadImageAsync(
            stream, file.FileName, $"carousel/{id}", null, ct);

        if (!uploadResult.Success)
            return StatusCode(StatusCodes.Status502BadGateway,
                new { error = new { code = "UPLOAD_FAILED", message = uploadResult.ErrorMessage } });

        var result = await mediator.Send(
            new UploadCarouselSlideImageCommand(id, uploadResult.PublicId!, uploadResult.SecureUrl!), ct);

        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    /// <summary>
    /// Removes the slide image without deleting the slide. The slide stops appearing on the
    /// public carousel until a new image is uploaded.
    /// </summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpDelete("{id:guid}/image")]
    [ProducesResponseType(typeof(CarouselSlideResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteImage(Guid id, CancellationToken ct)
    {
        var result = await mediator.Send(new DeleteCarouselSlideImageCommand(id), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }
}
