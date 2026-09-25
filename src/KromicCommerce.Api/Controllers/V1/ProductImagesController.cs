using Asp.Versioning;
using KromicCommerce.Application.Abstractions.Media;
using KromicCommerce.Application.Features.Catalog.Products.Images;
using KromicCommerce.Contracts.Catalog;
using Microsoft.AspNetCore.Authorization;

namespace KromicCommerce.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/products/{productId:guid}/images")]
[Authorize(Policy = "AdminOnly")]
public sealed class ProductImagesController(
    IMediator mediator,
    ICloudinaryService cloudinary) : ControllerBase
{
    private static readonly string[] AllowedMimeTypes =
        ["image/jpeg", "image/png", "image/webp", "image/gif", "image/avif"];

    /// <summary>Upload an image file and attach it to a product.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ProductImageDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> Upload(
        Guid productId,
        IFormFile file,
        [FromForm] string? altText,
        [FromForm] bool isPrimary = false,
        CancellationToken ct = default)
    {
        if (file.Length == 0)
            return BadRequest(new { error = new { code = "NO_FILE", message = "No file provided." } });

        if (!AllowedMimeTypes.Contains(file.ContentType.ToLowerInvariant()))
            return BadRequest(new { error = new { code = "INVALID_MIME_TYPE", message = "File type not allowed." } });

        await using var stream = file.OpenReadStream();
        var uploadResult = await cloudinary.UploadImageAsync(
            stream, file.FileName, $"products/{productId}", altText, ct);

        if (!uploadResult.Success)
            return StatusCode(StatusCodes.Status502BadGateway,
                new { error = new { code = "UPLOAD_FAILED", message = uploadResult.ErrorMessage } });

        var result = await mediator.Send(new AddProductImageCommand(
            productId,
            uploadResult.PublicId!,
            uploadResult.SecureUrl!,
            uploadResult.Format,
            uploadResult.Width,
            uploadResult.Height,
            altText,
            isPrimary), ct);

        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : result.Error.ToActionResult();
    }

    /// <summary>Reorder product images.</summary>
    [HttpPut("reorder")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Reorder(
        Guid productId,
        [FromBody] ReorderImagesRequest req,
        CancellationToken ct)
    {
        var result = await mediator.Send(new ReorderProductImagesCommand(productId, req.Items), ct);
        return result.IsSuccess ? NoContent() : result.Error.ToActionResult();
    }

    /// <summary>Delete a product image. Also removes it from Cloudinary.</summary>
    [HttpDelete("{imageId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid productId, Guid imageId, CancellationToken ct)
    {
        // Fetch asset publicId before deletion so we can remove from Cloudinary
        var result = await mediator.Send(new DeleteProductImageCommand(productId, imageId), ct);
        if (!result.IsSuccess) return result.Error.ToActionResult();
        // Note: Cloudinary deletion is best-effort here — a dedicated cleanup job handles orphaned assets.
        return NoContent();
    }
}
