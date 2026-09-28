using KromicCommerce.Application.Abstractions.Catalog;
using KromicCommerce.Application.Abstractions.Media;
using KromicCommerce.Application.Features.Catalog.Categories.CreateCategory;

namespace KromicCommerce.Application.Features.Catalog.Categories.Images;

internal sealed class UploadCategoryImageHandler(
    IApplicationDbContext db,
    ICloudinaryService cloudinary,
    ICatalogCacheService cache,
    ILogger<UploadCategoryImageHandler> logger)
    : ICommandHandler<UploadCategoryImageCommand, CategoryResponse>
{
    public async Task<Result<CategoryResponse>> Handle(
        UploadCategoryImageCommand cmd, CancellationToken cancellationToken)
    {
        var category = await db.Categories
            .Include(c => c.ParentCategory)
            .FirstOrDefaultAsync(c => c.Id == cmd.CategoryId, cancellationToken);

        if (category is null)
            return Result.Failure<CategoryResponse>(
                Error.NotFound("CATEGORY_NOT_FOUND", "Category not found."));

        // Capture old asset before overwriting — never delete it first.
        var oldPublicId = category.ImagePublicId;

        category.SetImage(cmd.PublicId, cmd.SecureUrl);
        await db.SaveChangesAsync(cancellationToken);

        cache.InvalidateCategories();
        cache.InvalidateStorefrontCategories();

        // Delete the old Cloudinary asset after the new reference is safely committed.
        // Failure is non-fatal: the DB is correct; the orphaned asset can be
        // cleaned up by a reconciliation job if needed.
        if (!string.IsNullOrWhiteSpace(oldPublicId))
        {
            var deleteResult = await cloudinary.DeleteAsync(oldPublicId, cancellationToken);
            if (!deleteResult.Success)
                logger.LogError(
                    "Cloudinary delete failed for old Category {CategoryId} image (PublicId: {PublicId}): {Error}",
                    cmd.CategoryId, oldPublicId, deleteResult.ErrorMessage);
            else
                logger.LogInformation(
                    "Category {CategoryId} image replaced. Old asset deleted. OldPublicId: {OldId}",
                    cmd.CategoryId, oldPublicId);
        }

        return Result.Success(CreateCategoryHandler.MapToResponse(
            category, category.ParentCategory?.Name));
    }
}

internal sealed class DeleteCategoryImageHandler(
    IApplicationDbContext db,
    ICloudinaryService cloudinary,
    ICatalogCacheService cache,
    ILogger<DeleteCategoryImageHandler> logger)
    : ICommandHandler<DeleteCategoryImageCommand>
{
    public async Task<Result> Handle(
        DeleteCategoryImageCommand cmd, CancellationToken cancellationToken)
    {
        var category = await db.Categories
            .FirstOrDefaultAsync(c => c.Id == cmd.CategoryId, cancellationToken);

        if (category is null)
            return Result.Failure(Error.NotFound("CATEGORY_NOT_FOUND", "Category not found."));

        if (string.IsNullOrWhiteSpace(category.ImagePublicId))
            return Result.Failure(Error.Validation("NO_IMAGE", "Category has no image to remove."));

        var publicId = category.ImagePublicId;

        category.ClearImage();
        await db.SaveChangesAsync(cancellationToken);

        cache.InvalidateCategories();
        cache.InvalidateStorefrontCategories();

        // Delete Cloudinary asset after DB commit — never before
        var deleteResult = await cloudinary.DeleteAsync(publicId, cancellationToken);
        if (!deleteResult.Success)
            logger.LogError(
                "Cloudinary delete failed for Category {CategoryId} image (PublicId: {PublicId}): {Error}",
                cmd.CategoryId, publicId, deleteResult.ErrorMessage);

        return Result.Success();
    }
}
