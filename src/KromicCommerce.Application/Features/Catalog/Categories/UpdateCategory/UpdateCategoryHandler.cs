using KromicCommerce.Application.Abstractions.Catalog;
using KromicCommerce.Application.Features.Catalog.Categories.CreateCategory;

namespace KromicCommerce.Application.Features.Catalog.Categories.UpdateCategory;

internal sealed class UpdateCategoryHandler(
    IApplicationDbContext db,
    ICatalogCacheService cache)
    : ICommandHandler<UpdateCategoryCommand, CategoryResponse>
{
    public async Task<Result<CategoryResponse>> Handle(UpdateCategoryCommand command, CancellationToken cancellationToken)
    {
        var category = await db.Categories.FindAsync([command.Id], cancellationToken);
        if (category is null)
            return Result.Failure<CategoryResponse>(Error.NotFound("CATEGORY_NOT_FOUND", "Category not found."));

        if (await db.Categories.AnyAsync(c => c.Slug == command.Slug && c.Id != command.Id, cancellationToken))
            return Result.Failure<CategoryResponse>(
                Error.Conflict("CATEGORY_SLUG_TAKEN", $"Slug '{command.Slug}' is already in use."));

        category.Update(command.Name, command.Slug, command.Description,
            command.ParentCategoryId, command.SortOrder);
        if (command.IsActive.HasValue)
        {
            if (command.IsActive.Value) category.Activate(); else category.Deactivate();
        }

        await db.SaveChangesAsync(cancellationToken);
        cache.InvalidateCategoryGraph();

        string? parentName = null;
        if (category.ParentCategoryId.HasValue)
        {
            parentName = await db.Categories
                .Where(c => c.Id == category.ParentCategoryId.Value)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return Result.Success(CreateCategoryHandler.MapToResponse(category, parentName));
    }
}
