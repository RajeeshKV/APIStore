using KromicCommerce.Application.Abstractions.Catalog;

namespace KromicCommerce.Application.Features.Catalog.Categories.DeleteCategory;

internal sealed class DeleteCategoryHandler(
    IApplicationDbContext db,
    ICatalogCacheService cache)
    : ICommandHandler<DeleteCategoryCommand>
{
    public async Task<Result> Handle(DeleteCategoryCommand command, CancellationToken cancellationToken)
    {
        var category = await db.Categories.FindAsync([command.Id], cancellationToken);
        if (category is null)
            return Result.Failure(Error.NotFound("CATEGORY_NOT_FOUND", "Category not found."));

        var hasProducts = await db.Products
            .AnyAsync(p => p.CategoryId == command.Id, cancellationToken);
        if (hasProducts)
            return Result.Failure(Error.Conflict("CATEGORY_HAS_PRODUCTS",
                "Cannot delete a category that has products. Move or reassign products first."));

        var hasChildren = await db.Categories
            .AnyAsync(c => c.ParentCategoryId == command.Id, cancellationToken);
        if (hasChildren)
            return Result.Failure(Error.Conflict("CATEGORY_HAS_CHILDREN",
                "Cannot delete a category with subcategories."));

        db.Categories.Remove(category);
        await db.SaveChangesAsync(cancellationToken);
        cache.InvalidateCategories();
        cache.InvalidateStorefrontCategories();
        return Result.Success();
    }
}
