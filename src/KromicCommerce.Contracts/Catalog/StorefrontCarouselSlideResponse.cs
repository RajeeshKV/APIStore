namespace KromicCommerce.Contracts.Catalog;

/// <summary>
/// A carousel slide as rendered on the storefront Home page.
/// </summary>
/// <remarks/// <para>
/// Deliberately narrower than <see cref="CarouselSlideResponse"/>: no Cloudinary public id, no
/// audit fields, no active flag, and no configurable link.
/// </para>
/// <para>
/// <see cref="CtaText"/> is a label only. <see cref="CtaTarget"/> is a fixed constant, not
/// per-slide configuration, so a slide can never become an arbitrary outbound link.
/// </para>
/// </remarks>
public sealed record StorefrontCarouselSlideResponse(
    Guid Id,
    string Title,
    string? Subtitle,
    string ImageUrl,
    string? CtaText,
    int SortOrder,
    string CtaTarget)
{
    /// <summary>
    /// The one destination every carousel CTA leads to. Emitted rather than stored so the
    /// storefront has a single authoritative value to navigate to.
    /// </summary>
    public const string FixedCtaTarget = "/shop";
}