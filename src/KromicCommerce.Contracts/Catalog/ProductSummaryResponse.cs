namespace KromicCommerce.Contracts.Catalog;

/// <summary>
/// Lightweight product representation for catalog listings.
/// Never includes inventory details — available flag is sufficient for storefront.
/// </summary>
public sealed record ProductSummaryResponse(
    Guid Id,
    string Name,
    string Slug,
    string? Sku,
    decimal Price,
    decimal? CompareAtPrice,
    string Status,
    Guid? CategoryId,
    string? CategoryName,
    Guid? BrandId,
    string? BrandName,
    bool IsFeatured,
    string? PrimaryImageUrl,
    bool IsAvailable,          // Available = has inventory > 0 (or no inventory tracking)
    DateTime UpdatedAtUtc);
