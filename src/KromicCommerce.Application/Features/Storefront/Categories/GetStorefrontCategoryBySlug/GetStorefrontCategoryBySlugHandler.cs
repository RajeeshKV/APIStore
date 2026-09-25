namespace KromicCommerce.Application.Features.Storefront.Categories.GetStorefrontCategoryBySlug;

internal sealed class GetStorefrontCategoryBySlugHandler(IApplicationDbContext db)
    : IQueryHandler<GetStorefrontCategoryBySlugQuery, StorefrontCategoryResponse>
{
    public async Task<Result<StorefrontCategoryResponse>> Handle(
        GetStorefrontCategoryBySlugQuery query,
        CancellationToken cancellationToken)
    {
        var slug = query.Slug.Trim().ToLowerInvariant();

        var c = await db.Categories
            .AsNoTracking()
            .Where(x => x.Slug == slug && x.IsActive)
            .Select(x => new
            {
                x.Id, x.Name, x.Slug, x.Description,
                x.ParentCategoryId, x.SortOrder, x.ImageUrl,
                ParentName = x.ParentCategory != null ? x.ParentCategory.Name : null,
                ProductCount = db.Products.Count(p =>
                    p.CategoryId == x.Id && p.Status == ProductStatus.Active)
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (c is null)
            return Result.Failure<StorefrontCategoryResponse>(
                Error.NotFound("CATEGORY_NOT_FOUND", "Category not found."));

        return Result.Success(new StorefrontCategoryResponse(
            c.Id, c.Name, c.Slug, c.Description,
            c.ParentCategoryId, c.ParentName,
            c.SortOrder, c.ImageUrl, c.ProductCount));
    }
}
