namespace KromicCommerce.Contracts.Catalog;

public sealed record CreateCategoryRequest(
    string Name,
    string Slug,
    string? Description,
    Guid? ParentCategoryId,
    int SortOrder = 0);
