namespace KromicCommerce.Domain.Store;

/// <summary>
/// Store-level SEO defaults.
/// Individual products and pages override these in later phases.
/// </summary>
public sealed class SeoSettings : ValueObject
{
    public static SeoSettings Default() => new();

    public string? MetaTitle { get; private set; }
    public string? MetaDescription { get; private set; }
    public string? MetaKeywords { get; private set; }

    /// <summary>Favicon URL (Cloudinary or static path).</summary>
    public string? FaviconUrl { get; private set; }

    /// <summary>Open Graph / social share image URL.</summary>
    public string? OgImageUrl { get; private set; }

    // -----------------------------------------------------------------------
    // Factory / update
    // -----------------------------------------------------------------------

    public static SeoSettings Create(
        string? metaTitle,
        string? metaDescription,
        string? metaKeywords,
        string? faviconUrl,
        string? ogImageUrl)
    {
        return new SeoSettings
        {
            MetaTitle = metaTitle?.Trim(),
            MetaDescription = metaDescription?.Trim(),
            MetaKeywords = metaKeywords?.Trim(),
            FaviconUrl = faviconUrl?.Trim(),
            OgImageUrl = ogImageUrl?.Trim()
        };
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return MetaTitle;
        yield return MetaDescription;
        yield return MetaKeywords;
        yield return FaviconUrl;
        yield return OgImageUrl;
    }
}
