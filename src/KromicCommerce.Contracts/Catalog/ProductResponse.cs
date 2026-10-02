using KromicCommerce.Domain.Catalog;

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
    ProductStatus Status,
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

    /// <summary>
    /// Mean rating across published reviews, to 2 decimal places. Read alongside
    /// <see cref="RatingCount"/>: zero with a zero count means unrated, not rated zero.
    /// </summary>
    decimal RatingAverage,

    /// <summary>Number of published reviews. Zero means the product has not been rated yet.</summary>
    int RatingCount,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc)
{
    /// <summary>True when at least one published review exists.</summary>
    public bool HasRatings => RatingCount > 0;
}
