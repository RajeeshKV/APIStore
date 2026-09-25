using KromicCommerce.Application.Abstractions.Catalog;

namespace KromicCommerce.Application.Features.Catalog.Brands.CreateBrand;

internal sealed class CreateBrandHandler(
    IApplicationDbContext db,
    ICatalogCacheService cache,
    ILogger<CreateBrandHandler> logger)
    : ICommandHandler<CreateBrandCommand, BrandResponse>
{
    public async Task<Result<BrandResponse>> Handle(
        CreateBrandCommand command,
        CancellationToken cancellationToken)
    {
        var slugExists = await db.Brands.AnyAsync(b => b.Slug == command.Slug, cancellationToken);
        if (slugExists)
            return Result.Failure<BrandResponse>(
                Error.Conflict("BRAND_SLUG_TAKEN", $"Slug '{command.Slug}' is already in use."));

        var brand = Brand.Create(command.Name, command.Slug, command.Description, command.WebsiteUrl);
        db.Brands.Add(brand);
        await db.SaveChangesAsync(cancellationToken);
        cache.InvalidateBrands();
        cache.InvalidateStorefrontBrands();

        logger.LogInformation("Brand created: {Slug}", brand.Slug);
        return Result.Success(MapToResponse(brand));
    }

    internal static BrandResponse MapToResponse(Brand b) =>
        new(b.Id, b.Name, b.Slug, b.Description, b.WebsiteUrl, b.IsActive, b.LogoUrl, b.UpdatedAtUtc);
}
