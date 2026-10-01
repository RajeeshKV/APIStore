namespace KromicCommerce.Contracts.Catalog;

/// <summary>
/// Creates a carousel slide. The image is uploaded separately via
/// <c>PUT /api/v1/admin/carousel/{id}/image</c>, following the same flow as categories and brands.
/// </summary>
public sealed record CreateCarouselSlideRequest(
    string Title,
    string? Subtitle,
    string? CtaText,
    int SortOrder = 0,
    bool IsActive = false);

/// <summary>
/// Updates a carousel slide. The image is not editable here — it is replaced through the image
/// upload endpoint, so a re-upload and a text edit cannot race each other.
/// </summary>
public sealed record UpdateCarouselSlideRequest(
    string Title,
    string? Subtitle,
    string? CtaText,
    int SortOrder,
    bool IsActive);