namespace KromicCommerce.Application.Features.Catalog.Products;

/// <summary>Shared mapping helpers for product responses. Kept internal — never exposed to API layer.</summary>
internal static class ProductMapper
{
    internal static ProductResponse MapToResponse(Product p) =>
        new(p.Id, p.Name, p.Slug, p.Sku, p.Description, p.ShortDescription,
            p.Price, p.CompareAtPrice, p.Status,
            p.CategoryId, p.Category?.Name, p.BrandId, p.Brand?.Name,
            p.IsFeatured, p.IsTaxable,
            p.MetaTitle, p.MetaDescription, p.MetaKeywords,
            p.Images.Select(i => new ProductImageDto(i.Id,
                new MediaAssetDto(i.Asset.PublicId, i.Asset.SecureUrl, i.Asset.Format,
                    i.Asset.Width, i.Asset.Height, i.Asset.AltText),
                i.SortOrder, i.IsPrimary)).ToList(),
            p.Attributes.OrderBy(a => a.SortOrder).Select(a => new ProductAttributeDto(
                a.Id, a.Name, a.SortOrder,
                a.Values.Select(v => new AttributeValueDto(v.Id, v.Value, v.SortOrder))
                    .OrderBy(v => v.SortOrder).ToList())).ToList(),
            // Variants are ordered explicitly, matching the storefront projection, so the
            // admin list and the customer-facing selector agree on display order.
            // Resolved attribute names are not available here (the product is loaded without
            // an attribute-value join), so Attributes is left null and the variant detail
            // endpoints supply it.
            p.Variants.OrderBy(v => v.SortOrder).ThenBy(v => v.CreatedAtUtc)
                .Select(v => new VariantResponse(
                    v.Id, v.Sku, v.PriceOverride, v.SortOrder, v.IsActive, v.AttributeValueIds,
                    null)).ToList(),
            p.CreatedAtUtc, p.UpdatedAtUtc);

    internal static ProductSummaryResponse MapToSummary(Product p, int? available = null) =>
        new(p.Id, p.Name, p.Slug, p.Sku, p.Price, p.CompareAtPrice, p.Status,
            p.CategoryId, p.Category?.Name, p.BrandId, p.Brand?.Name,
            p.IsFeatured,
            p.Images.OrderBy(i => i.SortOrder).FirstOrDefault(i => i.IsPrimary)?.Asset.SecureUrl
                ?? p.Images.OrderBy(i => i.SortOrder).FirstOrDefault()?.Asset.SecureUrl,
            available is null || available > 0,
            p.UpdatedAtUtc);
}
