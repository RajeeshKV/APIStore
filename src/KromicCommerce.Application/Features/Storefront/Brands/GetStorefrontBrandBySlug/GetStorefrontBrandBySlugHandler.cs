namespace KromicCommerce.Application.Features.Storefront.Brands.GetStorefrontBrandBySlug;

internal sealed class GetStorefrontBrandBySlugHandler(IApplicationDbContext db)
    : IQueryHandler<GetStorefrontBrandBySlugQuery, StorefrontBrandResponse>
{
    public async Task<Result<StorefrontBrandResponse>> Handle(
        GetStorefrontBrandBySlugQuery query,
        CancellationToken cancellationToken)
    {
        var slug = query.Slug.Trim().ToLowerInvariant();

        var b = await db.Brands
            .AsNoTracking()
            .Where(x => x.Slug == slug && x.IsActive)
            .Select(x => new
            {
                x.Id, x.Name, x.Slug, x.Description, x.WebsiteUrl, x.LogoUrl,
                ProductCount = db.Products.Count(p =>
                    p.BrandId == x.Id && p.Status == ProductStatus.Active)
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (b is null)
            return Result.Failure<StorefrontBrandResponse>(
                Error.NotFound("BRAND_NOT_FOUND", "Brand not found."));

        return Result.Success(new StorefrontBrandResponse(
            b.Id, b.Name, b.Slug, b.Description, b.WebsiteUrl, b.LogoUrl, b.ProductCount));
    }
}
