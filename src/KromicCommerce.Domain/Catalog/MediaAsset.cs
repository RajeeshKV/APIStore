namespace KromicCommerce.Domain.Catalog;

/// <summary>
/// Represents a Cloudinary-stored media asset.
/// PostgreSQL stores the metadata; binary data lives in Cloudinary only.
/// </summary>
public sealed class MediaAsset : ValueObject
{
    private MediaAsset() { }

    public static MediaAsset Create(
        string publicId,
        string secureUrl,
        string? format,
        int? width,
        int? height,
        string? altText)
    {
        if (string.IsNullOrWhiteSpace(publicId))
            throw new ArgumentException("PublicId is required.", nameof(publicId));
        if (string.IsNullOrWhiteSpace(secureUrl))
            throw new ArgumentException("SecureUrl is required.", nameof(secureUrl));

        return new MediaAsset
        {
            PublicId = publicId.Trim(),
            SecureUrl = secureUrl.Trim(),
            Format = format?.Trim(),
            Width = width,
            Height = height,
            AltText = altText?.Trim()
        };
    }

    /// <summary>Cloudinary public_id — used to reference/transform the asset.</summary>
    public string PublicId { get; private set; } = string.Empty;

    /// <summary>HTTPS URL returned by Cloudinary after upload.</summary>
    public string SecureUrl { get; private set; } = string.Empty;

    /// <summary>File format: jpg, png, webp, etc.</summary>
    public string? Format { get; private set; }

    public int? Width { get; private set; }
    public int? Height { get; private set; }
    public string? AltText { get; private set; }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return PublicId;
        yield return SecureUrl;
        yield return Format;
        yield return Width;
        yield return Height;
        yield return AltText;
    }
}
