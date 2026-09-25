namespace KromicCommerce.Contracts.Catalog;

public sealed record CategoryResponse(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    Guid? ParentCategoryId,
    string? ParentCategoryName,
    int SortOrder,
    bool IsActive,
    string? ImageUrl,
    DateTime UpdatedAtUtc);
