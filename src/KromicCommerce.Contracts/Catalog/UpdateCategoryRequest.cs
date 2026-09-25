namespace KromicCommerce.Contracts.Catalog;

public sealed record UpdateCategoryRequest(
    string Name,
    string Slug,
    string? Description,
    Guid? ParentCategoryId,
    int SortOrder,
    bool IsActive);
