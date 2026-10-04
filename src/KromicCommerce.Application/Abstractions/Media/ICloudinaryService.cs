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

    /// <summary>
    /// Uploads a video. Separate from <see cref="UploadImageAsync"/> because the two use
    /// different Cloudinary resource types, different validation ceilings and different
    /// duration limits — folding them into one method would mean a single set of rules that
    /// is wrong for at least one of them.
    /// </summary>
    Task<CloudinaryUploadResult> UploadVideoAsync(
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
    string? ErrorMessage)
{
    /// <summary>
    /// Video length in whole seconds, when the provider reports it. Null for images and for
    /// providers that do not probe duration synchronously.
    /// </summary>
    public int? DurationSeconds { get; init; }
}

public sealed record CloudinaryDeleteResult(
    bool Success,
    string? ErrorMessage);
