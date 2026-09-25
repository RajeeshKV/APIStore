using KromicCommerce.Application.Abstractions.Catalog;

namespace KromicCommerce.Application.Features.Catalog.Products.UpdateProduct;

internal sealed class UpdateProductHandler(
    IApplicationDbContext db,
    ICatalogCacheService cache)
    : ICommandHandler<UpdateProductCommand>
{
    public async Task<Result> Handle(UpdateProductCommand command, CancellationToken cancellationToken)
    {
        var product = await db.Products.FindAsync([command.Id], cancellationToken);
        if (product is null)
            return Result.Failure(Error.NotFound("PRODUCT_NOT_FOUND", "Product not found."));

        if (await db.Products.AnyAsync(p => p.Slug == command.Slug && p.Id != command.Id, cancellationToken))
            return Result.Failure(Error.Conflict("PRODUCT_SLUG_TAKEN", $"Slug '{command.Slug}' is already in use."));

        if (command.Sku is not null
            && await db.Products.AnyAsync(p => p.Sku == command.Sku && p.Id != command.Id, cancellationToken))
            return Result.Failure(Error.Conflict("PRODUCT_SKU_TAKEN", $"SKU '{command.Sku}' is already in use."));

        product.UpdateDetails(command.Name, command.Slug, command.Sku,
            command.Description, command.ShortDescription,
            command.CategoryId, command.BrandId, command.IsFeatured, command.IsTaxable);
        product.UpdatePricing(command.Price, command.CompareAtPrice);
        product.UpdateSeo(command.MetaTitle, command.MetaDescription, command.MetaKeywords);

        await db.SaveChangesAsync(cancellationToken);
        cache.InvalidateProduct(command.Id);
        cache.InvalidateStorefrontProduct(product.Slug);
        cache.InvalidateStorefrontFeatured();
        return Result.Success();
    }
}
