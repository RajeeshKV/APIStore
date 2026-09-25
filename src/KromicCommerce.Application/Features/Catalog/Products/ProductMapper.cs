namespace KromicCommerce.Application.Features.Catalog.Products;

/// <summary>Shared mapping helpers for product responses. Kept internal — never exposed to API layer.</summary>
internal static class ProductMapper
{
    internal static ProductResponse MapToResponse(Product p) =>
        new(p.Id, p.Name, p.Slug, p.Sku, p.Description, p.ShortDescription,
            p.Price, p.CompareAtPrice, p.Status.ToString(),
            p.CategoryId, p.Category?.Name, p.BrandId, p.Brand?.Name,
            p.IsFeatured, p.IsTaxable,
            p.MetaTitle, p.MetaDescription, p.MetaKeywords,
            p.Images.Select(i => new ProductImageDto(i.Id,
                new MediaAssetDto(i.Asset.PublicId, i.Asset.SecureUrl, i.Asset.Format,
                    i.Asset.Width, i.Asset.Height, i.Asset.AltText),
                i.SortOrder, i.IsPrimary)).ToList(),
            p.Attributes.Select(a => new ProductAttributeDto(a.Id, a.Name, a.SortOrder,
                a.Values.Select(v => new AttributeValueDto(v.Id, v.Value, v.SortOrder))
                    .OrderBy(v => v.SortOrder).ToList())).ToList(),
            p.Variants.Select(v => new VariantResponse(
                v.Id, v.Sku, v.PriceOverride, v.SortOrder, v.IsActive, v.AttributeValueIds,
                null)).ToList(),
            p.CreatedAtUtc, p.UpdatedAtUtc);

    internal static ProductSummaryResponse MapToSummary(Product p, int? available = null) =>
        new(p.Id, p.Name, p.Slug, p.Sku, p.Price, p.CompareAtPrice, p.Status.ToString(),
            p.CategoryId, p.Category?.Name, p.BrandId, p.Brand?.Name,
            p.IsFeatured,
            p.Images.OrderBy(i => i.SortOrder).FirstOrDefault(i => i.IsPrimary)?.Asset.SecureUrl
                ?? p.Images.OrderBy(i => i.SortOrder).FirstOrDefault()?.Asset.SecureUrl,
            available is null || available > 0,
            p.UpdatedAtUtc);
}
