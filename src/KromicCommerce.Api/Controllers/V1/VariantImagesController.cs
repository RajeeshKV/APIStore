using Asp.Versioning;
using KromicCommerce.Application.Abstractions.Media;
using KromicCommerce.Application.Features.Catalog.Products.Images;
using KromicCommerce.Application.Features.Catalog.Products.VariantImages;
using KromicCommerce.Contracts.Catalog;
using Microsoft.AspNetCore.Authorization;

namespace KromicCommerce.Api.Controllers.V1;

/// <summary>
/// Variant image management endpoints (admin only).
///
/// Variant images are stored alongside product images in the same Cloudinary folder
/// <c>products/{productId}</c> but carry a VariantId so they can be displayed only
/// for that combination. A product with no variants continues to use the existing
/// product-level image endpoints unchanged.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/products/{productId:guid}/variants/{variantId:guid}/images")]
[Authorize(Policy = "AdminOnly")]
public sealed class VariantImagesController(
    IMediator mediator,
    ICloudinaryService cloudinary) : ControllerBase
{
    private static readonly string[] AllowedMimeTypes =
        ["image/jpeg", "image/png", "image/webp", "image/gif", "image/avif"];

    private const int MaxFilesPerRequest = 10;

    /// <summary>
    /// Get all images for a variant.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ProductImageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        Guid productId,
        Guid variantId,
        CancellationToken ct)
    {
        var result = await mediator.Send(new GetVariantImagesQuery(productId, variantId), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    /// <summary>
    /// Upload one or more images and attach them to a variant.
    /// Files are uploaded to Cloudinary in order; the first image for the variant
    /// automatically becomes its primary. Subsequent images are appended.
    /// Maximum <c>10</c> files per request.
    /// Accepted MIME types: image/jpeg, image/png, image/webp, image/gif, image/avif.
    /// Maximum file size: 10 MB per file.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(IReadOnlyList<ProductImageDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> Upload(
        Guid productId,
        Guid variantId,
        IFormFileCollection files,
        [FromForm] string? altText = null,
        CancellationToken ct = default)
    {
        if (files.Count == 0)
            return BadRequest(new { error = new { code = "NO_FILES",
                message = "At least one file is required." } });

        if (files.Count > MaxFilesPerRequest)
            return BadRequest(new { error = new { code = "TOO_MANY_FILES",
                message = $"Maximum {MaxFilesPerRequest} files per request." } });

        foreach (var file in files)
        {
            if (file.Length == 0)
                return BadRequest(new { error = new { code = "EMPTY_FILE",
                    message = $"File '{file.FileName}' is empty." } });

            if (!AllowedMimeTypes.Contains(file.ContentType.ToLowerInvariant()))
                return BadRequest(new { error = new { code = "INVALID_MIME_TYPE",
                    message = $"File '{file.FileName}': type '{file.ContentType}' is not allowed." } });
        }

        var results = new List<ProductImageDto>(files.Count);

        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            await using var stream = file.OpenReadStream();

            var uploadResult = await cloudinary.UploadImageAsync(
                stream, file.FileName, $"products/{productId}", altText, ct);

            if (!uploadResult.Success)
                return StatusCode(StatusCodes.Status502BadGateway,
                    new { error = new { code = "UPLOAD_FAILED",
                        message = $"File '{file.FileName}': {uploadResult.ErrorMessage}" } });

            var result = await mediator.Send(new AddVariantImageCommand(
                productId,
                variantId,
                uploadResult.PublicId!,
                uploadResult.SecureUrl!,
                uploadResult.Format,
                uploadResult.Width,
                uploadResult.Height,
                altText,
                false), ct);

            if (!result.IsSuccess) return result.Error.ToActionResult();
            results.Add(result.Value);
        }

        return StatusCode(StatusCodes.Status201Created, results);
    }

    /// <summary>
    /// Update the display order of variant images.
    /// All image IDs belonging to the variant should be included.
    /// </summary>
    [HttpPut("reorder")]
    [ProducesResponseType(typeof(IReadOnlyList<ProductImageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reorder(
        Guid productId,
        Guid variantId,
        [FromBody] ReorderImagesRequest req,
        CancellationToken ct)
    {
        var result = await mediator.Send(
            new ReorderVariantImagesCommand(productId, variantId, req.Items), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    /// <summary>
    /// Set one variant image as the primary (hero) image for that variant.
    /// All other variant images are demoted automatically. The operation is atomic.
    /// </summary>
    [HttpPut("{imageId:guid}/set-primary")]
    [ProducesResponseType(typeof(ProductImageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetPrimary(
        Guid productId,
        Guid variantId,
        Guid imageId,
        CancellationToken ct)
    {
        var result = await mediator.Send(
            new SetPrimaryVariantImageCommand(productId, variantId, imageId), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    /// <summary>
    /// Delete a variant image.
    /// Removes the database record and deletes the asset from Cloudinary.
    /// If the deleted image was primary, the next image by sort order is promoted.
    /// </summary>
    [HttpDelete("{imageId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        Guid productId,
        Guid variantId,
        Guid imageId,
        CancellationToken ct)
    {
        var result = await mediator.Send(
            new DeleteVariantImageCommand(productId, variantId, imageId), ct);
        return result.IsSuccess ? NoContent() : result.Error.ToActionResult();
    }
}
