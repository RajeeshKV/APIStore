using KromicCommerce.Application.Abstractions.Catalog;
using KromicCommerce.Application.Abstractions.Media;

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
    : ICommandHandler<ReorderProductImagesCommand, IReadOnlyList<ProductImageDto>>
{
    public async Task<Result<IReadOnlyList<ProductImageDto>>> Handle(
        ReorderProductImagesCommand cmd, CancellationToken cancellationToken)
    {
        var productSlug = await db.Products
            .Where(p => p.Id == cmd.ProductId)
            .Select(p => p.Slug)
            .FirstOrDefaultAsync(cancellationToken);

        var images = await db.ProductImages
            .Where(i => i.ProductId == cmd.ProductId)
            .ToListAsync(cancellationToken);

        if (images.Count == 0)
            return Result.Failure<IReadOnlyList<ProductImageDto>>(
                Error.NotFound("PRODUCT_NOT_FOUND", "Product not found or has no images."));

        foreach (var item in cmd.Items)
        {
            var image = images.FirstOrDefault(i => i.Id == item.ImageId);
            if (image is null)
                return Result.Failure<IReadOnlyList<ProductImageDto>>(
                    Error.NotFound("IMAGE_NOT_FOUND", $"Image {item.ImageId} not found."));
            image.UpdateSortOrder(item.SortOrder);
        }

        await db.SaveChangesAsync(cancellationToken);
        cache.InvalidateProduct(cmd.ProductId);
        if (productSlug is not null) cache.InvalidateStorefrontProduct(productSlug);

        IReadOnlyList<ProductImageDto> result = images
            .OrderBy(i => i.SortOrder)
            .Select(i => new ProductImageDto(i.Id,
                new MediaAssetDto(i.Asset.PublicId, i.Asset.SecureUrl, i.Asset.Format,
                    i.Asset.Width, i.Asset.Height, i.Asset.AltText),
                i.SortOrder, i.IsPrimary))
            .ToList();

        return Result.Success(result);
    }
}

internal sealed class DeleteProductImageHandler(
    IApplicationDbContext db,
    ICloudinaryService cloudinary,
    ICatalogCacheService cache,
    ILogger<DeleteProductImageHandler> logger)
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

        var wasPrimary = image.IsPrimary;
        var publicId = image.Asset.PublicId;

        db.ProductImages.Remove(image);

        // If the deleted image was primary, promote the next image (lowest SortOrder)
        if (wasPrimary)
        {
            var nextImage = await db.ProductImages
                .Where(i => i.ProductId == cmd.ProductId && i.Id != cmd.ImageId)
                .OrderBy(i => i.SortOrder)
                .FirstOrDefaultAsync(cancellationToken);

            nextImage?.SetPrimary(true);
        }

        await db.SaveChangesAsync(cancellationToken);

        // Delete from Cloudinary after the DB commit — never delete the external asset first.
        // Failure is non-fatal: the DB record is already gone so the asset is orphaned
        // (better than pointing the DB at a deleted asset). Log for cleanup.
        var deleteResult = await cloudinary.DeleteAsync(publicId, cancellationToken);
        if (!deleteResult.Success)
            logger.LogError(
                "Cloudinary delete failed for ProductImage {ImageId} (PublicId: {PublicId}): {Error}",
                cmd.ImageId, publicId, deleteResult.ErrorMessage);

        cache.InvalidateProduct(cmd.ProductId);
        if (productSlug is not null) cache.InvalidateStorefrontProduct(productSlug);
        return Result.Success();
    }
}

internal sealed class SetPrimaryProductImageHandler(IApplicationDbContext db, ICatalogCacheService cache)
    : ICommandHandler<SetPrimaryProductImageCommand, ProductImageDto>
{
    public async Task<Result<ProductImageDto>> Handle(
        SetPrimaryProductImageCommand cmd, CancellationToken cancellationToken)
    {
        // Load all images for the product in one query
        var images = await db.ProductImages
            .Where(i => i.ProductId == cmd.ProductId)
            .ToListAsync(cancellationToken);

        if (images.Count == 0)
            return Result.Failure<ProductImageDto>(
                Error.NotFound("PRODUCT_NOT_FOUND", "Product not found or has no images."));

        var target = images.FirstOrDefault(i => i.Id == cmd.ImageId);
        if (target is null)
            return Result.Failure<ProductImageDto>(
                Error.NotFound("IMAGE_NOT_FOUND", "Image not found on this product."));

        // Atomically demote current primary and promote the target
        foreach (var img in images)
            img.SetPrimary(img.Id == cmd.ImageId);

        await db.SaveChangesAsync(cancellationToken);

        var productSlug = await db.Products
            .Where(p => p.Id == cmd.ProductId)
            .Select(p => p.Slug)
            .FirstOrDefaultAsync(cancellationToken);

        cache.InvalidateProduct(cmd.ProductId);
        if (productSlug is not null) cache.InvalidateStorefrontProduct(productSlug);

        return Result.Success(new ProductImageDto(
            target.Id,
            new MediaAssetDto(target.Asset.PublicId, target.Asset.SecureUrl, target.Asset.Format,
                target.Asset.Width, target.Asset.Height, target.Asset.AltText),
            target.SortOrder, true));
    }
}
