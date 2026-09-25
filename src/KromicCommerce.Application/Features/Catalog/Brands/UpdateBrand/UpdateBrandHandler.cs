using KromicCommerce.Application.Abstractions.Catalog;

namespace KromicCommerce.Application.Features.Catalog.Brands.UpdateBrand;

internal sealed class UpdateBrandHandler(
    IApplicationDbContext db,
    ICatalogCacheService cache)
    : ICommandHandler<UpdateBrandCommand>
{
    public async Task<Result> Handle(UpdateBrandCommand command, CancellationToken cancellationToken)
    {
        var brand = await db.Brands.FindAsync([command.Id], cancellationToken);
        if (brand is null)
            return Result.Failure(Error.NotFound("BRAND_NOT_FOUND", "Brand not found."));

        var slugConflict = await db.Brands
            .AnyAsync(b => b.Slug == command.Slug && b.Id != command.Id, cancellationToken);
        if (slugConflict)
            return Result.Failure(Error.Conflict("BRAND_SLUG_TAKEN", $"Slug '{command.Slug}' is already in use."));

        brand.Update(command.Name, command.Slug, command.Description, command.WebsiteUrl);
        if (command.IsActive) brand.Activate(); else brand.Deactivate();

        await db.SaveChangesAsync(cancellationToken);
        cache.InvalidateBrands();
        cache.InvalidateStorefrontBrands();
        return Result.Success();
    }
}
