using KromicCommerce.Application.Abstractions.Catalog;

namespace KromicCommerce.Application.Features.Catalog.Categories.UpdateCategory;

internal sealed class UpdateCategoryHandler(
    IApplicationDbContext db,
    ICatalogCacheService cache)
    : ICommandHandler<UpdateCategoryCommand>
{
    public async Task<Result> Handle(UpdateCategoryCommand command, CancellationToken cancellationToken)
    {
        var category = await db.Categories.FindAsync([command.Id], cancellationToken);
        if (category is null)
            return Result.Failure(Error.NotFound("CATEGORY_NOT_FOUND", "Category not found."));

        var slugConflict = await db.Categories
            .AnyAsync(c => c.Slug == command.Slug && c.Id != command.Id, cancellationToken);
        if (slugConflict)
            return Result.Failure(Error.Conflict("CATEGORY_SLUG_TAKEN", $"Slug '{command.Slug}' is already in use."));

        category.Update(command.Name, command.Slug, command.Description,
            command.ParentCategoryId, command.SortOrder);

        if (command.IsActive) category.Activate(); else category.Deactivate();

        await db.SaveChangesAsync(cancellationToken);
        cache.InvalidateCategories();
        cache.InvalidateStorefrontCategories();
        return Result.Success();
    }
}
