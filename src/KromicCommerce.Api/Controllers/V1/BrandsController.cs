using Asp.Versioning;
using KromicCommerce.Application.Abstractions.Media;
using KromicCommerce.Application.Features.Catalog.Brands.CreateBrand;
using KromicCommerce.Application.Features.Catalog.Brands.DeleteBrand;
using KromicCommerce.Application.Features.Catalog.Brands.GetBrands;
using KromicCommerce.Application.Features.Catalog.Brands.Images;
using KromicCommerce.Application.Features.Catalog.Brands.UpdateBrand;
using KromicCommerce.Contracts.Catalog;
using Microsoft.AspNetCore.Authorization;

namespace KromicCommerce.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/brands")]
public sealed class BrandsController(IMediator mediator, ICloudinaryService cloudinary)
    : ControllerBase
{
    private static readonly string[] AllowedMimeTypes =
        ["image/jpeg", "image/png", "image/webp", "image/gif", "image/avif"];

    // -----------------------------------------------------------------------
    // Read
    // -----------------------------------------------------------------------

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<BrandResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] bool activeOnly = true, CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetBrandsQuery(activeOnly), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    [HttpGet("{slug}")]
    [ProducesResponseType(typeof(BrandResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBySlug(string slug, CancellationToken ct)
    {
        var result = await mediator.Send(new GetBrandBySlugQuery(slug), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    // -----------------------------------------------------------------------
    // Mutations
    // -----------------------------------------------------------------------

    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(BrandResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateBrandRequest req, CancellationToken ct)
    {
        var result = await mediator.Send(
            new CreateBrandCommand(req.Name, req.Slug, req.Description, req.WebsiteUrl), ct);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetBySlug), new { slug = result.Value.Slug, version = "1" }, result.Value)
            : result.Error.ToActionResult();
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(BrandResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateBrandRequest req, CancellationToken ct)
    {
        var result = await mediator.Send(
            new UpdateBrandCommand(id, req.Name, req.Slug, req.Description, req.WebsiteUrl, req.IsActive), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await mediator.Send(new DeleteBrandCommand(id), ct);
        return result.IsSuccess ? NoContent() : result.Error.ToActionResult();
    }

    // -----------------------------------------------------------------------
    // Logo / image management
    // -----------------------------------------------------------------------

    /// <summary>
    /// Upload or replace the brand logo.
    /// If a logo already exists it is replaced: the new asset is persisted first,
    /// then the old Cloudinary asset is deleted — the database is never left pointing
    /// to a deleted asset.
    /// Accepted MIME types: image/jpeg, image/png, image/webp, image/gif, image/avif.
    /// Maximum file size: 10 MB.
    /// </summary>
    [HttpPut("{id:guid}/logo")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(BrandResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> UploadLogo(
        Guid id,
        IFormFile file,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = new { code = "NO_FILE", message = "No file provided." } });

        if (!AllowedMimeTypes.Contains(file.ContentType.ToLowerInvariant()))
            return BadRequest(new { error = new { code = "INVALID_MIME_TYPE",
                message = $"File type '{file.ContentType}' is not allowed." } });

        await using var stream = file.OpenReadStream();
        var uploadResult = await cloudinary.UploadImageAsync(
            stream, file.FileName, $"brands/{id}", null, ct);

        if (!uploadResult.Success)
            return StatusCode(StatusCodes.Status502BadGateway,
                new { error = new { code = "UPLOAD_FAILED", message = uploadResult.ErrorMessage } });

        var result = await mediator.Send(
            new UploadBrandLogoCommand(id, uploadResult.PublicId!, uploadResult.SecureUrl!), ct);

        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    /// <summary>
    /// Remove the brand logo without deleting the brand.
    /// The Cloudinary asset is deleted after the database record is cleared.
    /// </summary>
    [HttpDelete("{id:guid}/logo")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteLogo(Guid id, CancellationToken ct)
    {
        var result = await mediator.Send(new DeleteBrandLogoCommand(id), ct);
        return result.IsSuccess ? NoContent() : result.Error.ToActionResult();
    }
}
