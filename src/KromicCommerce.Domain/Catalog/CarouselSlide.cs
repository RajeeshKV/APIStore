namespace KromicCommerce.Domain.Catalog;

/// <summary>
/// A single slide in the storefront Home page carousel.
/// </summary>
/// <remarks>
/// The storefront always sends a carousel CTA to the Shop route. <see cref="CtaText"/> therefore
/// holds a label ("Shop Now") and never a destination: there is deliberately no URL field on this
/// entity, so an administrator cannot point a slide at an arbitrary site and the storefront cannot
/// be handed an open redirect.
/// <para>
/// Visibility is a simple <see cref="IsActive"/> flag. Scheduling windows are not modelled —
/// disabling a slide is how a campaign is retired, and adding date windows would introduce a
/// clock-dependent code path in the public query for a requirement that does not yet exist.
/// <para>
/// The image is uploaded after the slide is created, exactly as for categories and brands, so it
/// is optional on the entity. A slide without an image is a half-finished draft: the public query
/// skips it rather than rendering a broken hero image.
/// </para>
/// </remarks>
public sealed class CarouselSlide : AuditableEntity
{
    private CarouselSlide() { } // EF constructor

    public static CarouselSlide Create(
        string title,
        string? subtitle,
        string? ctaText,
        int sortOrder,
        bool isActive = false)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Carousel slide title is required.", nameof(title));

        return new CarouselSlide
        {
            Title = title.Trim(),
            Subtitle = subtitle?.Trim(),
            CtaText = ctaText?.Trim(),
            SortOrder = sortOrder,
            IsActive = isActive
        };
    }

    public string Title { get; private set; } = string.Empty;
    public string? Subtitle { get; private set; }

    /// <summary>Cloudinary public id, used to delete the asset later.</summary>
    public string? ImagePublicId { get; private set; }

    /// <summary>Cloudinary secure URL, returned to the storefront.</summary>
    public string? ImageUrl { get; private set; }

    /// <summary>Optional CTA label. The destination is always the storefront Shop route.</summary>
    public string? CtaText { get; private set; }

    /// <summary>Ascending display position. Lower values are shown first.</summary>
    public int SortOrder { get; private set; }

    /// <summary>Whether the storefront carousel should display this slide.</summary>
    public bool IsActive { get; private set; }

    // -----------------------------------------------------------------------
    // Behaviour
    // -----------------------------------------------------------------------

    public void Update(
        string title,
        string? subtitle,
        string? ctaText,
        int sortOrder,
        bool isActive)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Carousel slide title is required.", nameof(title));

        Title = title.Trim();
        Subtitle = subtitle?.Trim();
        CtaText = ctaText?.Trim();
        SortOrder = sortOrder;
        IsActive = isActive;
    }

    /// <summary>Replaces the image after a re-upload. Does not change visibility.</summary>
    public void SetImage(string publicId, string url)
    {
        if (string.IsNullOrWhiteSpace(publicId))
            throw new ArgumentException("Carousel slide image is required.", nameof(publicId));
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("Carousel slide image URL is required.", nameof(url));

        ImagePublicId = publicId.Trim();
        ImageUrl = url.Trim();
    }

    /// <summary>Removes the image, returning the slide to draft state.</summary>
    public void ClearImage()
    {
        ImagePublicId = null;
        ImageUrl = null;
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}