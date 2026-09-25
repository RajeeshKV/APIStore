namespace KromicCommerce.Application.Features.Catalog.Categories.UpdateCategory;

public sealed record UpdateCategoryCommand(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    Guid? ParentCategoryId,
    int SortOrder,
    bool IsActive) : ICommand;
