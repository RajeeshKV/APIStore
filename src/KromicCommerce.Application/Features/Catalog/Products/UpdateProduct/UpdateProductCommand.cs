namespace KromicCommerce.Application.Features.Catalog.Products.UpdateProduct;

public sealed record UpdateProductCommand(
    Guid Id,
    string Name,
    string Slug,
    string? Sku,
    decimal Price,
    decimal? CompareAtPrice,
    string? Description,
    string? ShortDescription,
    Guid? CategoryId,
    Guid? BrandId,
    bool IsFeatured,
    bool IsTaxable,
    string? MetaTitle,
    string? MetaDescription,
    string? MetaKeywords) : ICommand;
