using KromicCommerce.Contracts.Catalog;

namespace KromicCommerce.Application.Features.Catalog.Products.Variants;

internal sealed class GetVariantsHandler(IApplicationDbContext db)
    : IQueryHandler<GetVariantsQuery, IReadOnlyList<VariantResponse>>
{
    public async Task<Result<IReadOnlyList<VariantResponse>>> Handle(
        GetVariantsQuery query, CancellationToken ct)
    {
        var productExists = await db.Products.AnyAsync(p => p.Id == query.ProductId, ct);
        if (!productExists)
            return Result.Failure<IReadOnlyList<VariantResponse>>(
                Error.NotFound("PRODUCT_NOT_FOUND", "Product not found."));

        var variants = await db.ProductVariants
            .AsNoTracking()
            .Where(v => v.ProductId == query.ProductId)
            .OrderBy(v => v.SortOrder)
            .ThenBy(v => v.CreatedAtUtc)
            .ToListAsync(ct);

        if (!variants.Any())
            return Result.Success<IReadOnlyList<VariantResponse>>([]);

        // Batch-load inventory for all variants in one query
        var variantIds = variants.Select(v => v.Id).ToList();
        var inventoryMap = await db.InventoryItems
            .AsNoTracking()
            .Where(i => i.VariantId.HasValue && variantIds.Contains(i.VariantId.Value))
            .ToDictionaryAsync(i => i.VariantId!.Value, i => i.Available, ct);

        // Batch-load images for all variants
        var variantImages = await db.ProductImages
            .AsNoTracking()
            .Where(i => variantIds.Contains(i.VariantId!.Value))
            .OrderBy(i => i.VariantId).ThenBy(i => i.SortOrder)
            .ToListAsync(ct);
        var variantImagesMap = variantImages
            .GroupBy(i => i.VariantId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Resolve attribute values in one query for the whole product rather than per variant.
        var attributeMap = await VariantAttributeHelper.ResolveAsync(
            db, variants.SelectMany(v => v.ParsedAttributeValueIds).Distinct().ToList(), ct);

        var responses = variants.Select(v =>
        {
            var images = variantImagesMap.TryGetValue(v.Id, out var imgs)
                ? imgs.Select(i => new ProductImageDto(i.Id,
                    new MediaAssetDto(i.Asset.PublicId, i.Asset.SecureUrl, i.Asset.Format,
                        i.Asset.Width, i.Asset.Height, i.Asset.AltText),
                    i.SortOrder, i.IsPrimary)).ToList()
                : [];

            return new VariantResponse(
                v.Id, v.Sku, v.PriceOverride, v.SortOrder, v.IsActive, v.AttributeValueIds,
                inventoryMap.TryGetValue(v.Id, out var stock) ? stock : null,
                Attributes: v.ParsedAttributeValueIds
                    .Where(attributeMap.ContainsKey)
                    .Select(id => attributeMap[id])
                    .ToList(),
                Images: images);
        }).ToList();

        return Result.Success<IReadOnlyList<VariantResponse>>(responses);
    }
}

internal sealed class GetVariantByIdHandler(IApplicationDbContext db)
    : IQueryHandler<GetVariantByIdQuery, VariantResponse>
{
    public async Task<Result<VariantResponse>> Handle(
        GetVariantByIdQuery query, CancellationToken ct)
    {
        var variant = await db.ProductVariants
            .AsNoTracking()
            .Include(v => v.Images)
            .FirstOrDefaultAsync(
                v => v.Id == query.VariantId && v.ProductId == query.ProductId, ct);

        if (variant is null)
            return Result.Failure<VariantResponse>(
                Error.NotFound("VARIANT_NOT_FOUND", "Variant not found."));

        var inventoryItem = await db.InventoryItems
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.VariantId == variant.Id, ct);

        var attributes = await VariantAttributeResolution.ResolveAsync(db, variant, ct);

        var images = variant.Images
            .OrderBy(i => i.SortOrder)
            .Select(i => new ProductImageDto(i.Id,
                new MediaAssetDto(i.Asset.PublicId, i.Asset.SecureUrl, i.Asset.Format,
                    i.Asset.Width, i.Asset.Height, i.Asset.AltText),
                i.SortOrder, i.IsPrimary)).ToList();

        return Result.Success(new VariantResponse(
            variant.Id, variant.Sku, variant.PriceOverride, variant.SortOrder,
            variant.IsActive, variant.AttributeValueIds, inventoryItem?.Available, attributes,
            Images: images));
    }
}
