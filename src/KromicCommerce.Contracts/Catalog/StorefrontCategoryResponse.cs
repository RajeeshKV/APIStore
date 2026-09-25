namespace KromicCommerce.Contracts.Catalog;

/// <summary>
/// Category for storefront navigation and filtering.
/// ProductCount reflects active products only.
/// </summary>
public sealed record StorefrontCategoryResponse(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    Guid? ParentCategoryId,
    string? ParentCategoryName,
    int SortOrder,
    string? ImageUrl,

    /// <summary>Count of active (publicly visible) products in this category.</summary>
    int ProductCount);
