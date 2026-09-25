using KromicCommerce.Application.Abstractions.Catalog;

namespace KromicCommerce.Application.Features.Catalog.Brands.DeleteBrand;

internal sealed class DeleteBrandHandler(
    IApplicationDbContext db,
    ICatalogCacheService cache)
    : ICommandHandler<DeleteBrandCommand>
{
    public async Task<Result> Handle(DeleteBrandCommand command, CancellationToken cancellationToken)
    {
        var brand = await db.Brands.FindAsync([command.Id], cancellationToken);
        if (brand is null)
            return Result.Failure(Error.NotFound("BRAND_NOT_FOUND", "Brand not found."));

        var hasProducts = await db.Products.AnyAsync(p => p.BrandId == command.Id, cancellationToken);
        if (hasProducts)
            return Result.Failure(Error.Conflict("BRAND_HAS_PRODUCTS",
                "Cannot delete a brand with products. Reassign products first."));

        db.Brands.Remove(brand);
        await db.SaveChangesAsync(cancellationToken);
        cache.InvalidateBrands();
        cache.InvalidateStorefrontBrands();
        return Result.Success();
    }
}
