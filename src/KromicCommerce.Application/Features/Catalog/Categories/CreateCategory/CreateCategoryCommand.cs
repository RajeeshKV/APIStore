namespace KromicCommerce.Application.Features.Catalog.Categories.CreateCategory;

public sealed record CreateCategoryCommand(
    string Name,
    string Slug,
    string? Description,
    Guid? ParentCategoryId,
    int SortOrder) : ICommand<CategoryResponse>;
