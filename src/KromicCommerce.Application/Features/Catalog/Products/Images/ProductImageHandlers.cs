using KromicCommerce.Application.Abstractions.Catalog;

namespace KromicCommerce.Application.Features.Catalog.Products.Images;

internal sealed class AddProductImageHandler(IApplicationDbContext db, ICatalogCacheService cache)
    : ICommandHandler<AddProductImageCommand, ProductImageDto>
{
    public async Task<Result<ProductImageDto>> Handle(
        AddProductImageCommand cmd,
        CancellationToken cancellationToken)
    {
        var product = await db.Products
            .Where(p => p.Id == cmd.ProductId)
            .Select(p => new { p.Id, p.Slug })
            .FirstOrDefaultAsync(cancellationToken);

        if (product is null)
            return Result.Failure<ProductImageDto>(Error.NotFound("PRODUCT_NOT_FOUND", "Product not found."));

        var sortOrder = await db.ProductImages.CountAsync(
            i => i.ProductId == cmd.ProductId, cancellationToken);

        if (cmd.IsPrimary)
        {
            var existingImages = db.ProductImages.Where(i => i.ProductId == cmd.ProductId && i.IsPrimary);
            await existingImages.ForEachAsync(i => i.SetPrimary(false), cancellationToken);
        }

        var asset = MediaAsset.Create(cmd.PublicId, cmd.SecureUrl, cmd.Format, cmd.Width, cmd.Height, cmd.AltText);
        var image = ProductImage.Create(cmd.ProductId, asset, sortOrder, cmd.IsPrimary || sortOrder == 0);

        db.ProductImages.Add(image);

        // IMPORTANT: Cloudinary orphan-asset risk.
        // Cloudinary upload already succeeded before this command was dispatched.
        // If SaveChangesAsync fails, the Cloudinary asset remains orphaned.
        // A future Outbox/reconciliation job handles cleanup. Do not silently swallow.
        await db.SaveChangesAsync(cancellationToken);
        cache.InvalidateProduct(cmd.ProductId);
        cache.InvalidateStorefrontProduct(product.Slug);

        return Result.Success(new ProductImageDto(image.Id,
            new MediaAssetDto(asset.PublicId, asset.SecureUrl, asset.Format, asset.Width, asset.Height, asset.AltText),
            image.SortOrder, image.IsPrimary));
    }
}

internal sealed class ReorderProductImagesHandler(IApplicationDbContext db, ICatalogCacheService cache)
    : ICommandHandler<ReorderProductImagesCommand>
{
    public async Task<Result> Handle(ReorderProductImagesCommand cmd, CancellationToken cancellationToken)
    {
        var productSlug = await db.Products
            .Where(p => p.Id == cmd.ProductId)
            .Select(p => p.Slug)
            .FirstOrDefaultAsync(cancellationToken);

        var images = await db.ProductImages
            .Where(i => i.ProductId == cmd.ProductId)
            .ToListAsync(cancellationToken);

        if (images.Count == 0)
            return Result.Failure(Error.NotFound("PRODUCT_NOT_FOUND", "Product not found or has no images."));

        foreach (var item in cmd.Items)
        {
            var image = images.FirstOrDefault(i => i.Id == item.ImageId);
            if (image is null)
                return Result.Failure(Error.NotFound("IMAGE_NOT_FOUND", $"Image {item.ImageId} not found."));
            image.UpdateSortOrder(item.SortOrder);
        }

        await db.SaveChangesAsync(cancellationToken);
        cache.InvalidateProduct(cmd.ProductId);
        if (productSlug is not null) cache.InvalidateStorefrontProduct(productSlug);
        return Result.Success();
    }
}

internal sealed class DeleteProductImageHandler(IApplicationDbContext db, ICatalogCacheService cache)
    : ICommandHandler<DeleteProductImageCommand>
{
    public async Task<Result> Handle(DeleteProductImageCommand cmd, CancellationToken cancellationToken)
    {
        var image = await db.ProductImages
            .FirstOrDefaultAsync(
                i => i.Id == cmd.ImageId && i.ProductId == cmd.ProductId,
                cancellationToken);

        if (image is null)
            return Result.Failure(Error.NotFound("IMAGE_NOT_FOUND", "Image not found."));

        var productSlug = await db.Products
            .Where(p => p.Id == cmd.ProductId)
            .Select(p => p.Slug)
            .FirstOrDefaultAsync(cancellationToken);

        db.ProductImages.Remove(image);
        await db.SaveChangesAsync(cancellationToken);
        cache.InvalidateProduct(cmd.ProductId);
        if (productSlug is not null) cache.InvalidateStorefrontProduct(productSlug);
        return Result.Success();
    }
}
