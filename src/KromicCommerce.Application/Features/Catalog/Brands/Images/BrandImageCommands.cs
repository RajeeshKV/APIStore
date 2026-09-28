namespace KromicCommerce.Application.Features.Catalog.Brands.Images;

/// <summary>
/// Upload or replace the logo for a brand.
/// Safe replacement: the new asset is persisted first; the old asset is deleted after commit.
/// </summary>
public sealed record UploadBrandLogoCommand(
    Guid BrandId,
    string PublicId,
    string SecureUrl) : ICommand<BrandResponse>;

/// <summary>
/// Remove the brand logo without deleting the brand.
/// The Cloudinary asset is deleted after the DB record is cleared.
/// </summary>
public sealed record DeleteBrandLogoCommand(
    Guid BrandId) : ICommand;
