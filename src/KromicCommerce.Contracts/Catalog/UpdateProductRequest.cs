namespace KromicCommerce.Contracts.Catalog;

public sealed record UpdateProductRequest(
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
    string? MetaKeywords);
