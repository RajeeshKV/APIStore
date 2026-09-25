namespace KromicCommerce.Contracts.Catalog;

public sealed record CreateProductRequest(
    string Name,
    string Slug,
    string? Sku,
    decimal Price,
    decimal? CompareAtPrice,
    string? Description,
    string? ShortDescription,
    Guid? CategoryId,
    Guid? BrandId,
    bool IsFeatured = false,
    bool IsTaxable = true,
    string? MetaTitle = null,
    string? MetaDescription = null,
    string? MetaKeywords = null);
