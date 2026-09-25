using KromicCommerce.Application.Abstractions.Catalog;

namespace KromicCommerce.Application.Features.Catalog.Categories.CreateCategory;

internal sealed class CreateCategoryHandler(
    IApplicationDbContext db,
    ICatalogCacheService cache,
    ILogger<CreateCategoryHandler> logger)
    : ICommandHandler<CreateCategoryCommand, CategoryResponse>
{
    public async Task<Result<CategoryResponse>> Handle(
        CreateCategoryCommand command,
        CancellationToken cancellationToken)
    {
        var slugExists = await db.Categories
            .AnyAsync(c => c.Slug == command.Slug, cancellationToken);
        if (slugExists)
            return Result.Failure<CategoryResponse>(
                Error.Conflict("CATEGORY_SLUG_TAKEN", $"Slug '{command.Slug}' is already in use."));

        var category = Category.Create(
            command.Name, command.Slug, command.Description,
            command.ParentCategoryId, command.SortOrder);

        db.Categories.Add(category);
        await db.SaveChangesAsync(cancellationToken);
        cache.InvalidateCategories();
        cache.InvalidateStorefrontCategories();

        logger.LogInformation("Category created: {Slug}", category.Slug);

        return Result.Success(MapToResponse(category, null));
    }

    internal static CategoryResponse MapToResponse(Category c, string? parentName) =>
        new(c.Id, c.Name, c.Slug, c.Description, c.ParentCategoryId, parentName,
            c.SortOrder, c.IsActive, c.ImageUrl, c.UpdatedAtUtc);
}
