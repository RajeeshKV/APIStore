namespace KromicCommerce.Application.Abstractions.Media;

/// <summary>
/// Media upload/delete abstraction.
/// SDK types must not leak into Domain or Application — only <see cref="CloudinaryUploadResult"/>
/// and <see cref="CloudinaryDeleteResult"/> cross the boundary.
/// </summary>
public interface ICloudinaryService
{
    /// <summary>
    /// Uploads a stream to Cloudinary under the given folder.
    /// Validates MIME type and size before sending.
    /// </summary>
    Task<CloudinaryUploadResult> UploadImageAsync(
        Stream stream,
        string fileName,
        string folder,
        string? altText = null,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes an asset by its Cloudinary public_id.</summary>
    Task<CloudinaryDeleteResult> DeleteAsync(
        string publicId,
        CancellationToken cancellationToken = default);
}

public sealed record CloudinaryUploadResult(
    bool Success,
    string? PublicId,
    string? SecureUrl,
    string? Format,
    int? Width,
    int? Height,
    string? ErrorMessage);

public sealed record CloudinaryDeleteResult(
    bool Success,
    string? ErrorMessage);
