namespace KromicCommerce.Infrastructure.Configuration;

public sealed class CloudinaryOptions
{
    public const string SectionName = "Cloudinary";

    [Required(AllowEmptyStrings = false, ErrorMessage = "Cloudinary:CloudName is required.")]
    public string CloudName { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = false, ErrorMessage = "Cloudinary:ApiKey is required.")]
    public string ApiKey { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = false, ErrorMessage = "Cloudinary:ApiSecret is required.")]
    public string ApiSecret { get; init; } = string.Empty;

    /// <summary>Optional folder prefix for all uploaded assets. E.g. "kromic-store".</summary>
    public string? UploadFolder { get; init; }
}
