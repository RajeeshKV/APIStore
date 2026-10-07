using KromicCommerce.Application.Abstractions.Catalog;
using KromicCommerce.Application.Abstractions.Media;
using KromicCommerce.Contracts.Catalog;

namespace KromicCommerce.Application.Features.Catalog.Products.VariantImages;

internal sealed class GetVariantImagesHandler(IApplicationDbContext db)
    : IQueryHandler<GetVariantImagesQuery, IReadOnlyList<ProductImageDto>>
{
    public async Task<Result<IReadOnlyList<ProductImageDto>>> Handle(
        GetVariantImagesQuery query, CancellationToken cancellationToken)
    {
        var variant = await db.ProductVariants
            .Where(v => v.Id == query.VariantId && v.ProductId == query.ProductId)
            .Select(v => new { v.Id })
            .FirstOrDefaultAsync(cancellationToken);

        if (variant is null)
            return Result.Failure<IReadOnlyList<ProductImageDto>>(
                Error.NotFound("VARIANT_NOT_FOUND", "Variant not found."));

        var images = await db.ProductImages
            .AsNoTracking()
            .Where(i => i.ProductId == query.ProductId && i.VariantId == query.VariantId)
            .OrderBy(i => i.SortOrder)
            .ToListAsync(cancellationToken);

        var result = images
            .Select(i => new ProductImageDto(i.Id,
                new MediaAssetDto(i.Asset.PublicId, i.Asset.SecureUrl, i.Asset.Format,
                    i.Asset.Width, i.Asset.Height, i.Asset.AltText),
                i.SortOrder, i.IsPrimary))
            .ToList();

        return Result.Success<IReadOnlyList<ProductImageDto>>(result);
    }
}

internal sealed class AddVariantImageHandler(
    IApplicationDbContext db,
    ICatalogCacheService cache)
    : ICommandHandler<AddVariantImageCommand, ProductImageDto>
{
    public async Task<Result<ProductImageDto>> Handle(
        AddVariantImageCommand cmd,
        CancellationToken cancellationToken)
    {
        var variant = await db.ProductVariants
            .Where(v => v.Id == cmd.VariantId && v.ProductId == cmd.ProductId)
            .Select(v => new { v.Id })
            .FirstOrDefaultAsync(cancellationToken);

        if (variant is null)
            return Result.Failure<ProductImageDto>(
                Error.NotFound("VARIANT_NOT_FOUND", "Variant not found."));

        var product = await db.Products
            .Where(p => p.Id == cmd.ProductId)
            .Select(p => new { p.Slug })
            .FirstOrDefaultAsync(cancellationToken);

        var sortOrder = await db.ProductImages.CountAsync(
            i => i.ProductId == cmd.ProductId && i.VariantId == cmd.VariantId,
            cancellationToken);

        if (cmd.IsPrimary)
        {
            var existingImages = db.ProductImages
                .Where(i => i.ProductId == cmd.ProductId && i.VariantId == cmd.VariantId && i.IsPrimary);
            await existingImages.ForEachAsync(i => i.SetPrimary(false), cancellationToken);
        }

        var asset = MediaAsset.Create(
            cmd.PublicId, cmd.SecureUrl, cmd.Format, cmd.Width, cmd.Height, cmd.AltText);

        var image = ProductImage.Create(
            cmd.ProductId, asset, sortOrder, cmd.IsPrimary || sortOrder == 0, cmd.VariantId);

        db.ProductImages.Add(image);
        await db.SaveChangesAsync(cancellationToken);

        cache.InvalidateProductGraph(cmd.ProductId, product?.Slug);

        return Result.Success(new ProductImageDto(image.Id,
            new MediaAssetDto(asset.PublicId, asset.SecureUrl, asset.Format,
                asset.Width, asset.Height, asset.AltText),
            image.SortOrder, image.IsPrimary));
    }
}

internal sealed class ReorderVariantImagesHandler(
    IApplicationDbContext db,
    ICatalogCacheService cache)
    : ICommandHandler<ReorderVariantImagesCommand, IReadOnlyList<ProductImageDto>>
{
    public async Task<Result<IReadOnlyList<ProductImageDto>>> Handle(
        ReorderVariantImagesCommand cmd,
        CancellationToken cancellationToken)
    {
        var productSlug = await db.Products
            .Where(p => p.Id == cmd.ProductId)
            .Select(p => p.Slug)
            .FirstOrDefaultAsync(cancellationToken);

        var images = await db.ProductImages
            .Where(i => i.ProductId == cmd.ProductId && i.VariantId == cmd.VariantId)
            .ToListAsync(cancellationToken);

        if (images.Count == 0)
            return Result.Failure<IReadOnlyList<ProductImageDto>>(
                Error.NotFound("VARIANT_NOT_FOUND", "Variant not found or has no images."));

        foreach (var item in cmd.Items)
        {
            var image = images.FirstOrDefault(i => i.Id == item.ImageId);
            if (image is null)
                return Result.Failure<IReadOnlyList<ProductImageDto>>(
                    Error.NotFound("IMAGE_NOT_FOUND", $"Image {item.ImageId} not found on this variant."));
            image.UpdateSortOrder(item.SortOrder);
        }

        await db.SaveChangesAsync(cancellationToken);
        cache.InvalidateProductGraph(cmd.ProductId, productSlug);

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

internal sealed class DeleteVariantImageHandler(
    IApplicationDbContext db,
    ICloudinaryService cloudinary,
    ICatalogCacheService cache,
    ILogger<DeleteVariantImageHandler> logger)
    : ICommandHandler<DeleteVariantImageCommand>
{
    public async Task<Result> Handle(
        DeleteVariantImageCommand cmd,
        CancellationToken cancellationToken)
    {
        var image = await db.ProductImages
            .FirstOrDefaultAsync(
                i => i.Id == cmd.ImageId
                     && i.ProductId == cmd.ProductId
                     && i.VariantId == cmd.VariantId,
                cancellationToken);

        if (image is null)
            return Result.Failure(Error.NotFound("IMAGE_NOT_FOUND", "Image not found on this variant."));

        var productSlug = await db.Products
            .Where(p => p.Id == cmd.ProductId)
            .Select(p => p.Slug)
            .FirstOrDefaultAsync(cancellationToken);

        var wasPrimary = image.IsPrimary;
        var publicId = image.Asset.PublicId;

        db.ProductImages.Remove(image);

        if (wasPrimary)
        {
            var nextImage = await db.ProductImages
                .Where(i => i.ProductId == cmd.ProductId
                         && i.VariantId == cmd.VariantId
                         && i.Id != cmd.ImageId)
                .OrderBy(i => i.SortOrder)
                .FirstOrDefaultAsync(cancellationToken);

            nextImage?.SetPrimary(true);
        }

        await db.SaveChangesAsync(cancellationToken);

        var deleteResult = await cloudinary.DeleteAsync(publicId, cancellationToken);
        if (!deleteResult.Success)
            logger.LogError(
                "Cloudinary delete failed for VariantImage {ImageId} (PublicId: {PublicId}): {Error}",
                cmd.ImageId, publicId, deleteResult.ErrorMessage);

        cache.InvalidateProductGraph(cmd.ProductId, productSlug);
        return Result.Success();
    }
}

internal sealed class SetPrimaryVariantImageHandler(
    IApplicationDbContext db,
    ICatalogCacheService cache)
    : ICommandHandler<SetPrimaryVariantImageCommand, ProductImageDto>
{
    public async Task<Result<ProductImageDto>> Handle(
        SetPrimaryVariantImageCommand cmd,
        CancellationToken cancellationToken)
    {
        var images = await db.ProductImages
            .Where(i => i.ProductId == cmd.ProductId && i.VariantId == cmd.VariantId)
            .ToListAsync(cancellationToken);

        if (images.Count == 0)
            return Result.Failure<ProductImageDto>(
                Error.NotFound("VARIANT_NOT_FOUND", "Variant not found or has no images."));

        var target = images.FirstOrDefault(i => i.Id == cmd.ImageId);
        if (target is null)
            return Result.Failure<ProductImageDto>(
                Error.NotFound("IMAGE_NOT_FOUND", "Image not found on this variant."));

        foreach (var img in images)
            img.SetPrimary(img.Id == cmd.ImageId);

        await db.SaveChangesAsync(cancellationToken);

        var productSlug = await db.Products
            .Where(p => p.Id == cmd.ProductId)
            .Select(p => p.Slug)
            .FirstOrDefaultAsync(cancellationToken);

        cache.InvalidateProductGraph(cmd.ProductId, productSlug);

        return Result.Success(new ProductImageDto(
            target.Id,
            new MediaAssetDto(target.Asset.PublicId, target.Asset.SecureUrl, target.Asset.Format,
                target.Asset.Width, target.Asset.Height, target.Asset.AltText),
            target.SortOrder, true));
    }
}
