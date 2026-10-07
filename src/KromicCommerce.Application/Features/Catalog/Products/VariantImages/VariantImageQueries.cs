namespace KromicCommerce.Application.Features.Catalog.Products.VariantImages;

public sealed record GetVariantImagesQuery(
    Guid ProductId,
    Guid VariantId) : IQuery<IReadOnlyList<ProductImageDto>>;