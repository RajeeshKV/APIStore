namespace KromicCommerce.Contracts.Catalog;

/// <summary>
/// Admin representation of a carousel slide. Includes the Cloudinary public id and audit
/// timestamps, which the storefront never sees.
/// </summary>
public sealed record CarouselSlideResponse(
    Guid Id,
    string Title,
    string? Subtitle,
    string? ImagePublicId,
    string? ImageUrl,
    string? CtaText,
    int SortOrder,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);