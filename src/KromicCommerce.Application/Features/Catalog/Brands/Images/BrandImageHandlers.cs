using KromicCommerce.Application.Abstractions.Catalog;
using KromicCommerce.Application.Abstractions.Media;
using KromicCommerce.Application.Features.Catalog.Brands.CreateBrand;

namespace KromicCommerce.Application.Features.Catalog.Brands.Images;

internal sealed class UploadBrandLogoHandler(
    IApplicationDbContext db,
    ICloudinaryService cloudinary,
    ICatalogCacheService cache,
    ILogger<UploadBrandLogoHandler> logger)
    : ICommandHandler<UploadBrandLogoCommand, BrandResponse>
{
    public async Task<Result<BrandResponse>> Handle(
        UploadBrandLogoCommand cmd, CancellationToken cancellationToken)
    {
        var brand = await db.Brands
            .FirstOrDefaultAsync(b => b.Id == cmd.BrandId, cancellationToken);

        if (brand is null)
            return Result.Failure<BrandResponse>(
                Error.NotFound("BRAND_NOT_FOUND", "Brand not found."));

        // Capture old asset before overwriting — never delete it first.
        var oldPublicId = brand.LogoPublicId;

        brand.SetLogo(cmd.PublicId, cmd.SecureUrl);
        await db.SaveChangesAsync(cancellationToken);

        cache.InvalidateBrands();
        cache.InvalidateStorefrontBrands();

        if (!string.IsNullOrWhiteSpace(oldPublicId))
        {
            var deleteResult = await cloudinary.DeleteAsync(oldPublicId, cancellationToken);
            if (!deleteResult.Success)
                logger.LogError(
                    "Cloudinary delete failed for old Brand {BrandId} logo (PublicId: {PublicId}): {Error}",
                    cmd.BrandId, oldPublicId, deleteResult.ErrorMessage);
            else
                logger.LogInformation(
                    "Brand {BrandId} logo replaced. Old asset deleted. OldPublicId: {OldId}",
                    cmd.BrandId, oldPublicId);
        }

        return Result.Success(CreateBrandHandler.MapToResponse(brand));
    }
}

internal sealed class DeleteBrandLogoHandler(
    IApplicationDbContext db,
    ICloudinaryService cloudinary,
    ICatalogCacheService cache,
    ILogger<DeleteBrandLogoHandler> logger)
    : ICommandHandler<DeleteBrandLogoCommand>
{
    public async Task<Result> Handle(
        DeleteBrandLogoCommand cmd, CancellationToken cancellationToken)
    {
        var brand = await db.Brands
            .FirstOrDefaultAsync(b => b.Id == cmd.BrandId, cancellationToken);

        if (brand is null)
            return Result.Failure(Error.NotFound("BRAND_NOT_FOUND", "Brand not found."));

        if (string.IsNullOrWhiteSpace(brand.LogoPublicId))
            return Result.Failure(Error.Validation("NO_LOGO", "Brand has no logo to remove."));

        var publicId = brand.LogoPublicId;

        brand.ClearLogo();
        await db.SaveChangesAsync(cancellationToken);

        cache.InvalidateBrands();
        cache.InvalidateStorefrontBrands();

        // Delete Cloudinary asset after DB commit — never before
        var deleteResult = await cloudinary.DeleteAsync(publicId, cancellationToken);
        if (!deleteResult.Success)
            logger.LogError(
                "Cloudinary delete failed for Brand {BrandId} logo (PublicId: {PublicId}): {Error}",
                cmd.BrandId, publicId, deleteResult.ErrorMessage);

        return Result.Success();
    }
}
