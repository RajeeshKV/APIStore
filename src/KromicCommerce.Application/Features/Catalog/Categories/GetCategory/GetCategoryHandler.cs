using KromicCommerce.Application.Features.Catalog.Categories.CreateCategory;

namespace KromicCommerce.Application.Features.Catalog.Categories.GetCategory;

internal sealed class GetCategoryBySlugHandler(IApplicationDbContext db)
    : IQueryHandler<GetCategoryBySlugQuery, CategoryResponse>
{
    public async Task<Result<CategoryResponse>> Handle(
        GetCategoryBySlugQuery query, CancellationToken cancellationToken)
    {
        var c = await db.Categories
            .AsNoTracking()
            .Include(x => x.ParentCategory)
            .FirstOrDefaultAsync(x => x.Slug == query.Slug, cancellationToken);

        return c is null
            ? Result.Failure<CategoryResponse>(Error.NotFound("CATEGORY_NOT_FOUND", "Category not found."))
            : Result.Success(CreateCategoryHandler.MapToResponse(c, c.ParentCategory?.Name));
    }
}

internal sealed class GetCategoryByIdHandler(IApplicationDbContext db)
    : IQueryHandler<GetCategoryByIdQuery, CategoryResponse>
{
    public async Task<Result<CategoryResponse>> Handle(
        GetCategoryByIdQuery query, CancellationToken cancellationToken)
    {
        var c = await db.Categories
            .AsNoTracking()
            .Include(x => x.ParentCategory)
            .FirstOrDefaultAsync(x => x.Id == query.Id, cancellationToken);

        return c is null
            ? Result.Failure<CategoryResponse>(Error.NotFound("CATEGORY_NOT_FOUND", "Category not found."))
            : Result.Success(CreateCategoryHandler.MapToResponse(c, c.ParentCategory?.Name));
    }
}
