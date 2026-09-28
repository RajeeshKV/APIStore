namespace KromicCommerce.Application.Features.Catalog.Products.Images;

public sealed record AddProductImageCommand(
    Guid ProductId,
    string PublicId,
    string SecureUrl,
    string? Format,
    int? Width,
    int? Height,
    string? AltText,
    bool IsPrimary) : ICommand<ProductImageDto>;

public sealed record ReorderProductImagesCommand(
    Guid ProductId,
    IReadOnlyList<ImageSortOrderItem> Items) : ICommand<IReadOnlyList<ProductImageDto>>;

public sealed record DeleteProductImageCommand(
    Guid ProductId,
    Guid ImageId) : ICommand;

/// <summary>
/// Sets one product image as the primary (hero) image.
/// Atomically demotes the current primary and promotes the selected image.
/// </summary>
public sealed record SetPrimaryProductImageCommand(
    Guid ProductId,
    Guid ImageId) : ICommand<ProductImageDto>;
