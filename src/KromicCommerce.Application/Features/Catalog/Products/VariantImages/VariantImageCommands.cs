namespace KromicCommerce.Application.Features.Catalog.Products.VariantImages;

public sealed record AddVariantImageCommand(
    Guid ProductId,
    Guid VariantId,
    string PublicId,
    string SecureUrl,
    string? Format,
    int? Width,
    int? Height,
    string? AltText,
    bool IsPrimary) : ICommand<ProductImageDto>;

public sealed record ReorderVariantImagesCommand(
    Guid ProductId,
    Guid VariantId,
    IReadOnlyList<ImageSortOrderItem> Items) : ICommand<IReadOnlyList<ProductImageDto>>;

public sealed record DeleteVariantImageCommand(
    Guid ProductId,
    Guid VariantId,
    Guid ImageId) : ICommand;

public sealed record SetPrimaryVariantImageCommand(
    Guid ProductId,
    Guid VariantId,
    Guid ImageId) : ICommand<ProductImageDto>;
