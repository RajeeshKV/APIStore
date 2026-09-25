namespace KromicCommerce.Domain.Catalog;

/// <summary>
/// Product brand. Slug must be unique (enforced by DB unique index).
/// </summary>
public sealed class Brand : AuditableEntity
{
    private Brand() { } // EF constructor

    public static Brand Create(string name, string slug, string? description, string? websiteUrl)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Brand name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(slug))
            throw new ArgumentException("Brand slug is required.", nameof(slug));

        return new Brand
        {
            Name = name.Trim(),
            Slug = slug.Trim().ToLowerInvariant(),
            Description = description?.Trim(),
            WebsiteUrl = websiteUrl?.Trim(),
            IsActive = true
        };
    }

    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string? WebsiteUrl { get; private set; }
    public bool IsActive { get; private set; }

    /// <summary>Cloudinary logo asset.</summary>
    public string? LogoPublicId { get; private set; }
    public string? LogoUrl { get; private set; }

    // -----------------------------------------------------------------------
    // Behaviour
    // -----------------------------------------------------------------------

    public void Update(string name, string slug, string? description, string? websiteUrl)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name required.", nameof(name));
        if (string.IsNullOrWhiteSpace(slug)) throw new ArgumentException("Slug required.", nameof(slug));
        Name = name.Trim();
        Slug = slug.Trim().ToLowerInvariant();
        Description = description?.Trim();
        WebsiteUrl = websiteUrl?.Trim();
    }

    public void SetLogo(string publicId, string url)
    {
        LogoPublicId = publicId.Trim();
        LogoUrl = url.Trim();
    }

    public void ClearLogo()
    {
        LogoPublicId = null;
        LogoUrl = null;
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}
