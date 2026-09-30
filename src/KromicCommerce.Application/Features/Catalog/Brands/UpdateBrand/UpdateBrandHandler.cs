using KromicCommerce.Application.Abstractions.Catalog;
using KromicCommerce.Application.Features.Catalog.Brands.CreateBrand;

namespace KromicCommerce.Application.Features.Catalog.Brands.UpdateBrand;

internal sealed class UpdateBrandHandler(
    IApplicationDbContext db,
    ICatalogCacheService cache)
    : ICommandHandler<UpdateBrandCommand, BrandResponse>
{
    public async Task<Result<BrandResponse>> Handle(UpdateBrandCommand command, CancellationToken cancellationToken)
    {
        var brand = await db.Brands.FindAsync([command.Id], cancellationToken);
        if (brand is null)
            return Result.Failure<BrandResponse>(Error.NotFound("BRAND_NOT_FOUND", "Brand not found."));

        if (await db.Brands.AnyAsync(b => b.Slug == command.Slug && b.Id != command.Id, cancellationToken))
            return Result.Failure<BrandResponse>(
                Error.Conflict("BRAND_SLUG_TAKEN", $"Slug '{command.Slug}' is already in use."));

        brand.Update(command.Name, command.Slug, command.Description, command.WebsiteUrl);
        if (command.IsActive.HasValue)
        {
            if (command.IsActive.Value) brand.Activate(); else brand.Deactivate();
        }

        await db.SaveChangesAsync(cancellationToken);
        cache.InvalidateBrands();
        cache.InvalidateStorefrontBrands();

        return Result.Success(CreateBrandHandler.MapToResponse(brand));
    }
}
