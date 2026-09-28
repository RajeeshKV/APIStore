namespace KromicCommerce.Application.Features.Catalog.Categories.Images;

/// <summary>
/// Upload or replace the image for a category.
/// The Cloudinary upload is performed in the controller before this command is dispatched.
/// Safe replacement: the new asset is persisted first; the old asset is deleted after commit.
/// </summary>
public sealed record UploadCategoryImageCommand(
    Guid CategoryId,
    string PublicId,
    string SecureUrl) : ICommand<CategoryResponse>;

/// <summary>
/// Remove the category image without deleting the category.
/// The Cloudinary asset is deleted after the DB record is cleared.
/// </summary>
public sealed record DeleteCategoryImageCommand(
    Guid CategoryId) : ICommand;
