namespace KromicCommerce.Contracts.Catalog;

/// <summary>Full product detail response including images, attributes, and variants.</summary>
public sealed record ProductResponse(
    Guid Id,
    string Name,
    string Slug,
    string? Sku,
    string? Description,
    string? ShortDescription,
    decimal Price,
    decimal? CompareAtPrice,
    string Status,
    Guid? CategoryId,
    string? CategoryName,
    Guid? BrandId,
    string? BrandName,
    bool IsFeatured,
    bool IsTaxable,
    string? MetaTitle,
    string? MetaDescription,
    string? MetaKeywords,
    IReadOnlyList<ProductImageDto> Images,
    IReadOnlyList<ProductAttributeDto> Attributes,
    IReadOnlyList<VariantResponse> Variants,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
