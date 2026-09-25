using KromicCommerce.Application.Abstractions.Catalog;

namespace KromicCommerce.Application.Features.Catalog.Products.Variants;

internal sealed class CreateVariantHandler(IApplicationDbContext db, ICatalogCacheService cache)
    : ICommandHandler<CreateVariantCommand, VariantResponse>
{
    public async Task<Result<VariantResponse>> Handle(
        CreateVariantCommand cmd, CancellationToken ct)
    {
        var productSlug = await db.Products
            .Where(p => p.Id == cmd.ProductId)
            .Select(p => p.Slug)
            .FirstOrDefaultAsync(ct);

        if (productSlug is null)
            return Result.Failure<VariantResponse>(Error.NotFound("PRODUCT_NOT_FOUND", "Product not found."));

        if (cmd.Sku is not null &&
            await db.ProductVariants.AnyAsync(v => v.Sku == cmd.Sku, ct))
            return Result.Failure<VariantResponse>(
                Error.Conflict("VARIANT_SKU_TAKEN", $"SKU '{cmd.Sku}' is already in use."));

        var variant = ProductVariant.Create(cmd.ProductId, cmd.Sku, cmd.PriceOverride, cmd.SortOrder);
        if (cmd.AttributeValueIds?.Count > 0)
            variant.SetAttributeValues(cmd.AttributeValueIds);

        db.ProductVariants.Add(variant);
        await db.SaveChangesAsync(ct);
        cache.InvalidateProduct(cmd.ProductId);
        cache.InvalidateStorefrontProduct(productSlug);

        return Result.Success(new VariantResponse(
            variant.Id, variant.Sku, variant.PriceOverride,
            variant.SortOrder, variant.IsActive, variant.AttributeValueIds, null));
    }
}

internal sealed class UpdateVariantHandler(IApplicationDbContext db, ICatalogCacheService cache)
    : ICommandHandler<UpdateVariantCommand>
{
    public async Task<Result> Handle(UpdateVariantCommand cmd, CancellationToken ct)
    {
        var variant = await db.ProductVariants
            .FirstOrDefaultAsync(v => v.Id == cmd.VariantId && v.ProductId == cmd.ProductId, ct);
        if (variant is null)
            return Result.Failure(Error.NotFound("VARIANT_NOT_FOUND", "Variant not found."));

        if (cmd.Sku is not null &&
            await db.ProductVariants.AnyAsync(v => v.Sku == cmd.Sku && v.Id != cmd.VariantId, ct))
            return Result.Failure(Error.Conflict("VARIANT_SKU_TAKEN", $"SKU '{cmd.Sku}' is already in use."));

        variant.Update(cmd.Sku, cmd.PriceOverride, cmd.SortOrder);
        if (cmd.AttributeValueIds?.Count > 0) variant.SetAttributeValues(cmd.AttributeValueIds);
        if (cmd.IsActive) variant.Activate(); else variant.Deactivate();

        var productSlug = await db.Products
            .Where(p => p.Id == cmd.ProductId)
            .Select(p => p.Slug)
            .FirstOrDefaultAsync(ct);

        await db.SaveChangesAsync(ct);
        cache.InvalidateProduct(cmd.ProductId);
        if (productSlug is not null) cache.InvalidateStorefrontProduct(productSlug);
        return Result.Success();
    }
}

internal sealed class DeleteVariantHandler(IApplicationDbContext db, ICatalogCacheService cache)
    : ICommandHandler<DeleteVariantCommand>
{
    public async Task<Result> Handle(DeleteVariantCommand cmd, CancellationToken ct)
    {
        var variant = await db.ProductVariants
            .FirstOrDefaultAsync(v => v.Id == cmd.VariantId && v.ProductId == cmd.ProductId, ct);
        if (variant is null)
            return Result.Failure(Error.NotFound("VARIANT_NOT_FOUND", "Variant not found."));

        var productSlug = await db.Products
            .Where(p => p.Id == cmd.ProductId)
            .Select(p => p.Slug)
            .FirstOrDefaultAsync(ct);

        db.ProductVariants.Remove(variant);
        await db.SaveChangesAsync(ct);
        cache.InvalidateProduct(cmd.ProductId);
        if (productSlug is not null) cache.InvalidateStorefrontProduct(productSlug);
        return Result.Success();
    }
}
