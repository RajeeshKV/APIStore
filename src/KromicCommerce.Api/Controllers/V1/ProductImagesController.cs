using Asp.Versioning;
using KromicCommerce.Application.Abstractions.Media;
using KromicCommerce.Application.Features.Catalog.Products.Images;
using KromicCommerce.Contracts.Catalog;
using Microsoft.AspNetCore.Authorization;

namespace KromicCommerce.Api.Controllers.V1;

/// <summary>
/// Product image management endpoints (admin only).
///
/// Upload supports multiple files in a single request.
/// Files are uploaded to Cloudinary in sequence and each is registered as a
/// ProductImage. The first upload for a product automatically becomes primary.
/// Existing images may be reordered and one may be explicitly set as primary.
/// </summary>
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

    private const int MaxFilesPerRequest = 10;

    // -----------------------------------------------------------------------
    // Upload — supports 1..N files in a single multipart/form-data request
    // -----------------------------------------------------------------------

    /// <summary>
    /// Upload one or more images and attach them to the product.
    /// Files are uploaded to Cloudinary in order; the first image for a product
    /// automatically becomes primary. Subsequent images are appended.
    /// Maximum <c>10</c> files per request.
    /// Accepted MIME types: image/jpeg, image/png, image/webp, image/gif, image/avif.
    /// Maximum file size: 10 MB per file.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(IReadOnlyList<ProductImageDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> Upload(
        Guid productId,
        IFormFileCollection files,
        [FromForm] string? altText = null,
        CancellationToken ct = default)
    {
        if (files.Count == 0)
            return BadRequest(new { error = new { code = "NO_FILES", message = "At least one file is required." } });

        if (files.Count > MaxFilesPerRequest)
            return BadRequest(new { error = new { code = "TOO_MANY_FILES",
                message = $"Maximum {MaxFilesPerRequest} files per request." } });

        // Validate all files before touching Cloudinary — fail fast
        foreach (var file in files)
        {
            if (file.Length == 0)
                return BadRequest(new { error = new { code = "EMPTY_FILE",
                    message = $"File '{file.FileName}' is empty." } });

            if (!AllowedMimeTypes.Contains(file.ContentType.ToLowerInvariant()))
                return BadRequest(new { error = new { code = "INVALID_MIME_TYPE",
                    message = $"File '{file.FileName}': type '{file.ContentType}' is not allowed." } });
        }

        // Upload each file to Cloudinary and register it as a ProductImage
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

            // isPrimary only for the very first file of the batch and only if the
            // product has no images yet — AddProductImageHandler auto-promotes
            // the first image (sortOrder == 0) regardless of this flag.
            var result = await mediator.Send(new AddProductImageCommand(
                productId,
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

    // -----------------------------------------------------------------------
    // Reorder
    // -----------------------------------------------------------------------

    /// <summary>
    /// Update the display order of product images.
    /// All image IDs belonging to the product should be included.
    /// </summary>
    [HttpPut("reorder")]
    [ProducesResponseType(typeof(IReadOnlyList<ProductImageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reorder(
        Guid productId,
        [FromBody] ReorderImagesRequest req,
        CancellationToken ct)
    {
        var result = await mediator.Send(new ReorderProductImagesCommand(productId, req.Items), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    // -----------------------------------------------------------------------
    // Set primary image
    // -----------------------------------------------------------------------

    /// <summary>
    /// Set one image as the primary (hero) image for the product.
    /// All other images are demoted automatically. The operation is atomic.
    /// </summary>
    [HttpPut("{imageId:guid}/set-primary")]
    [ProducesResponseType(typeof(ProductImageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetPrimary(
        Guid productId,
        Guid imageId,
        CancellationToken ct)
    {
        var result = await mediator.Send(new SetPrimaryProductImageCommand(productId, imageId), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    // -----------------------------------------------------------------------
    // Delete
    // -----------------------------------------------------------------------

    /// <summary>
    /// Delete a product image.
    /// Removes the database record and deletes the asset from Cloudinary.
    /// If the deleted image was primary, the next image by sort order is promoted.
    /// </summary>
    [HttpDelete("{imageId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid productId, Guid imageId, CancellationToken ct)
    {
        var result = await mediator.Send(new DeleteProductImageCommand(productId, imageId), ct);
        return result.IsSuccess ? NoContent() : result.Error.ToActionResult();
    }
}
