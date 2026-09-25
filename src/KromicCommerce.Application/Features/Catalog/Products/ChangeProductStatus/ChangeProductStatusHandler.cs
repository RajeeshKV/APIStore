using KromicCommerce.Application.Abstractions.Catalog;

namespace KromicCommerce.Application.Features.Catalog.Products.ChangeProductStatus;

internal sealed class PublishProductHandler(IApplicationDbContext db, ICatalogCacheService cache)
    : ICommandHandler<PublishProductCommand>
{
    public async Task<Result> Handle(PublishProductCommand cmd, CancellationToken ct)
    {
        var product = await db.Products.FindAsync([cmd.ProductId], ct);
        if (product is null) return Result.Failure(Error.NotFound("PRODUCT_NOT_FOUND", "Product not found."));
        product.Publish();
        await db.SaveChangesAsync(ct);
        cache.InvalidateProduct(cmd.ProductId);
        cache.InvalidateStorefrontProduct(product.Slug);
        cache.InvalidateStorefrontFeatured();
        return Result.Success();
    }
}

internal sealed class ArchiveProductHandler(IApplicationDbContext db, ICatalogCacheService cache)
    : ICommandHandler<ArchiveProductCommand>
{
    public async Task<Result> Handle(ArchiveProductCommand cmd, CancellationToken ct)
    {
        var product = await db.Products.FindAsync([cmd.ProductId], ct);
        if (product is null) return Result.Failure(Error.NotFound("PRODUCT_NOT_FOUND", "Product not found."));
        product.Archive();
        await db.SaveChangesAsync(ct);
        cache.InvalidateProduct(cmd.ProductId);
        cache.InvalidateStorefrontProduct(product.Slug);
        cache.InvalidateStorefrontFeatured();
        return Result.Success();
    }
}

internal sealed class UnpublishProductHandler(IApplicationDbContext db, ICatalogCacheService cache)
    : ICommandHandler<UnpublishProductCommand>
{
    public async Task<Result> Handle(UnpublishProductCommand cmd, CancellationToken ct)
    {
        var product = await db.Products.FindAsync([cmd.ProductId], ct);
        if (product is null) return Result.Failure(Error.NotFound("PRODUCT_NOT_FOUND", "Product not found."));
        product.Unpublish();
        await db.SaveChangesAsync(ct);
        cache.InvalidateProduct(cmd.ProductId);
        cache.InvalidateStorefrontProduct(product.Slug);
        cache.InvalidateStorefrontFeatured();
        return Result.Success();
    }
}
