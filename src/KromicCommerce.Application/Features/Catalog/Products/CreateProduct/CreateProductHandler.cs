using KromicCommerce.Application.Abstractions.Catalog;

namespace KromicCommerce.Application.Features.Catalog.Products.CreateProduct;

internal sealed class CreateProductHandler(
    IApplicationDbContext db,
    ICatalogCacheService cache,
    ILogger<CreateProductHandler> logger)
    : ICommandHandler<CreateProductCommand, ProductResponse>
{
    public async Task<Result<ProductResponse>> Handle(
        CreateProductCommand command,
        CancellationToken cancellationToken)
    {
        var slugExists = await db.Products.AnyAsync(p => p.Slug == command.Slug, cancellationToken);
        if (slugExists)
            return Result.Failure<ProductResponse>(
                Error.Conflict("PRODUCT_SLUG_TAKEN", $"Slug '{command.Slug}' is already in use."));

        if (command.Sku is not null)
        {
            var skuExists = await db.Products.AnyAsync(
                p => p.Sku == command.Sku, cancellationToken);
            if (skuExists)
                return Result.Failure<ProductResponse>(
                    Error.Conflict("PRODUCT_SKU_TAKEN", $"SKU '{command.Sku}' is already in use."));
        }

        var product = Product.Create(
            command.Name, command.Slug, command.Sku, command.Price,
            command.CategoryId, command.BrandId);

        product.UpdateDetails(
            command.Name, command.Slug, command.Sku,
            command.Description, command.ShortDescription,
            command.CategoryId, command.BrandId,
            command.IsFeatured, command.IsTaxable);

        product.UpdatePricing(command.Price, command.CompareAtPrice);
        product.UpdateSeo(command.MetaTitle, command.MetaDescription, command.MetaKeywords);

        db.Products.Add(product);
        await db.SaveChangesAsync(cancellationToken);
        cache.InvalidateProducts();

        logger.LogInformation("Product created: {Slug}", product.Slug);
        return Result.Success(ProductMapper.MapToResponse(product));
    }
}
