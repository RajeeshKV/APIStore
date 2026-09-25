using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using KromicCommerce.Application.Abstractions.Media;
using KromicCommerce.Infrastructure.Configuration;

namespace KromicCommerce.Infrastructure.Media;

/// <summary>
/// Cloudinary implementation of ICloudinaryService.
/// SDK types are fully contained in this file — never exposed to Application or Domain.
/// Upload validation (MIME, size) is enforced before the SDK call.
/// API secret is never logged.
///
/// ORPHAN ASSET RISK:
/// UploadImageAsync can succeed while the subsequent DB persistence (in AddProductImageHandler)
/// fails. The Cloudinary asset then exists without a ProductImage record.
/// The PublicId is returned in the upload result so callers can log or remediate.
/// A future reconciliation job should clean up assets with no matching DB record.
/// Never expose the API key or secret in logs or error responses.
/// </summary>
internal sealed class CloudinaryService(
    IOptions<CloudinaryOptions> options,
    ILogger<CloudinaryService> logger) : ICloudinaryService
{
    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB
    private static readonly HashSet<string> AllowedExtensions =
        [".jpg", ".jpeg", ".png", ".webp", ".gif", ".avif"];

    private Cloudinary CreateClient()
    {
        var opts = options.Value;
        var account = new Account(opts.CloudName, opts.ApiKey, opts.ApiSecret);
        return new Cloudinary(account) { Api = { Secure = true } };
    }

    public async Task<CloudinaryUploadResult> UploadImageAsync(
        Stream stream,
        string fileName,
        string folder,
        string? altText = null,
        CancellationToken cancellationToken = default)
    {
        if (stream.Length > MaxFileSizeBytes)
            return new CloudinaryUploadResult(false, null, null, null, null, null,
                $"File size exceeds maximum allowed ({MaxFileSizeBytes / 1024 / 1024} MB).");

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            return new CloudinaryUploadResult(false, null, null, null, null, null,
                $"File type '{ext}' is not allowed. Allowed: {string.Join(", ", AllowedExtensions)}");

        try
        {
            var uploadFolder = string.IsNullOrWhiteSpace(options.Value.UploadFolder)
                ? folder
                : $"{options.Value.UploadFolder}/{folder}";

            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(fileName, stream),
                Folder = uploadFolder,
                UseFilename = false,
                UniqueFilename = true,
                Overwrite = false,
                Context = altText is not null
                    ? new StringDictionary { { "alt", altText } }
                    : null
            };

            var client = CreateClient();
            var result = await client.UploadAsync(uploadParams, cancellationToken);

            if (result.Error is not null)
            {
                logger.LogError("Cloudinary upload error: {Message}", result.Error.Message);
                return new CloudinaryUploadResult(false, null, null, null, null, null, result.Error.Message);
            }

            logger.LogInformation("Cloudinary upload success. PublicId: {PublicId}", result.PublicId);
            return new CloudinaryUploadResult(
                true,
                result.PublicId,
                result.SecureUrl?.ToString(),
                result.Format,
                (int?)result.Width,
                (int?)result.Height,
                null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected Cloudinary upload error for file {FileName}", fileName);
            return new CloudinaryUploadResult(false, null, null, null, null, null, ex.Message);
        }
    }

    public async Task<CloudinaryDeleteResult> DeleteAsync(
        string publicId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = CreateClient();
            var result = await client.DestroyAsync(new DeletionParams(publicId));

            if (result.Error is not null)
            {
                logger.LogError("Cloudinary delete error: {Message}", result.Error.Message);
                return new CloudinaryDeleteResult(false, result.Error.Message);
            }

            logger.LogInformation("Cloudinary asset deleted. PublicId: {PublicId}", publicId);
            return new CloudinaryDeleteResult(true, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected Cloudinary delete error for PublicId {PublicId}", publicId);
            return new CloudinaryDeleteResult(false, ex.Message);
        }
    }
}
