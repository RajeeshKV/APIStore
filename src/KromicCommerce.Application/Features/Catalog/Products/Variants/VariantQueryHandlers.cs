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

        var responses = variants.Select(v => new VariantResponse(
            v.Id, v.Sku, v.PriceOverride, v.SortOrder, v.IsActive, v.AttributeValueIds,
            inventoryMap.TryGetValue(v.Id, out var stock) ? stock : null))
            .ToList();

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
            .FirstOrDefaultAsync(
                v => v.Id == query.VariantId && v.ProductId == query.ProductId, ct);

        if (variant is null)
            return Result.Failure<VariantResponse>(
                Error.NotFound("VARIANT_NOT_FOUND", "Variant not found."));

        var inventoryItem = await db.InventoryItems
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.VariantId == variant.Id, ct);

        return Result.Success(new VariantResponse(
            variant.Id, variant.Sku, variant.PriceOverride, variant.SortOrder,
            variant.IsActive, variant.AttributeValueIds, inventoryItem?.Available));
    }
}
