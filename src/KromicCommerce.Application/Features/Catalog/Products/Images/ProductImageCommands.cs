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
    IReadOnlyList<ImageSortOrderItem> Items) : ICommand;

public sealed record DeleteProductImageCommand(
    Guid ProductId,
    Guid ImageId) : ICommand;
