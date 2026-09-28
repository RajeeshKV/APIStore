using KromicCommerce.Application.Abstractions.Catalog;

namespace KromicCommerce.Application.Features.Catalog.Products.Variants;

internal sealed class CreateVariantHandler(IApplicationDbContext db, ICatalogCacheService cache)
    : ICommandHandler<CreateVariantCommand, VariantResponse>
{
    public async Task<Result<VariantResponse>> Handle(
        CreateVariantCommand cmd, CancellationToken ct)
    {
        // Product existence + slug (needed for cache invalidation)
        var product = await db.Products
            .Where(p => p.Id == cmd.ProductId)
            .Select(p => new { p.Slug, p.IsFeatured })
            .FirstOrDefaultAsync(ct);

        if (product is null)
            return Result.Failure<VariantResponse>(Error.NotFound("PRODUCT_NOT_FOUND", "Product not found."));

        // SKU uniqueness (global partial unique index — also checked at DB level)
        if (cmd.Sku is not null &&
            await db.ProductVariants.AnyAsync(v => v.Sku == cmd.Sku, ct))
            return Result.Failure<VariantResponse>(
                Error.Conflict("VARIANT_SKU_TAKEN", $"SKU '{cmd.Sku}' is already in use."));

        // Attribute-value validation
        if (cmd.AttributeValueIds?.Count > 0)
        {
            var attrValidation = await VariantAttributeHelper.ValidateAttributeValues(
                db, cmd.ProductId, cmd.AttributeValueIds, null, ct);
            if (attrValidation is not null)
                return Result.Failure<VariantResponse>(attrValidation);
        }

        // Duplicate combination check (order-insensitive)
        if (cmd.AttributeValueIds?.Count > 0)
        {
            var dupCheck = await VariantAttributeHelper.CheckDuplicateCombination(
                db, cmd.ProductId, null, cmd.AttributeValueIds, ct);
            if (dupCheck is not null)
                return Result.Failure<VariantResponse>(dupCheck);
        }

        var variant = ProductVariant.Create(cmd.ProductId, cmd.Sku, cmd.PriceOverride, cmd.SortOrder);
        if (cmd.AttributeValueIds?.Count > 0)
            variant.SetAttributeValues(VariantAttributeHelper.SortedIds(cmd.AttributeValueIds));

        db.ProductVariants.Add(variant);

        // Auto-create an InventoryItem at 0 stock so the variant appears in inventory management
        var inventoryItem = InventoryItem.Create(cmd.ProductId, variant.Id, onHand: 0);
        db.InventoryItems.Add(inventoryItem);

        await db.SaveChangesAsync(ct);

        cache.InvalidateProduct(cmd.ProductId);
        cache.InvalidateStorefrontProduct(product.Slug);
        if (product.IsFeatured) cache.InvalidateStorefrontFeatured();

        return Result.Success(new VariantResponse(
            variant.Id, variant.Sku, variant.PriceOverride,
            variant.SortOrder, variant.IsActive, variant.AttributeValueIds,
            AvailableStock: 0));
    }
}

internal sealed class UpdateVariantHandler(IApplicationDbContext db, ICatalogCacheService cache)
    : ICommandHandler<UpdateVariantCommand, VariantResponse>
{
    public async Task<Result<VariantResponse>> Handle(UpdateVariantCommand cmd, CancellationToken ct)
    {
        var variant = await db.ProductVariants
            .FirstOrDefaultAsync(v => v.Id == cmd.VariantId && v.ProductId == cmd.ProductId, ct);
        if (variant is null)
            return Result.Failure<VariantResponse>(Error.NotFound("VARIANT_NOT_FOUND", "Variant not found."));

        // SKU uniqueness (exclude self)
        if (cmd.Sku is not null &&
            await db.ProductVariants.AnyAsync(v => v.Sku == cmd.Sku && v.Id != cmd.VariantId, ct))
            return Result.Failure<VariantResponse>(Error.Conflict("VARIANT_SKU_TAKEN", $"SKU '{cmd.Sku}' is already in use."));

        // Attribute-value validation
        if (cmd.AttributeValueIds?.Count > 0)
        {
            var attrValidation = await VariantAttributeHelper.ValidateAttributeValues(
                db, cmd.ProductId, cmd.AttributeValueIds, cmd.VariantId, ct);
            if (attrValidation is not null)
                return Result.Failure<VariantResponse>(attrValidation);

            var dupCheck = await VariantAttributeHelper.CheckDuplicateCombination(
                db, cmd.ProductId, cmd.VariantId, cmd.AttributeValueIds, ct);
            if (dupCheck is not null)
                return Result.Failure<VariantResponse>(dupCheck);
        }

        variant.Update(cmd.Sku, cmd.PriceOverride, cmd.SortOrder);
        if (cmd.AttributeValueIds?.Count > 0)
            variant.SetAttributeValues(VariantAttributeHelper.SortedIds(cmd.AttributeValueIds));
        if (cmd.IsActive) variant.Activate(); else variant.Deactivate();

        var product = await db.Products
            .Where(p => p.Id == cmd.ProductId)
            .Select(p => new { p.Slug, p.IsFeatured })
            .FirstOrDefaultAsync(ct);

        await db.SaveChangesAsync(ct);

        cache.InvalidateProduct(cmd.ProductId);
        if (product is not null)
        {
            cache.InvalidateStorefrontProduct(product.Slug);
            if (product.IsFeatured) cache.InvalidateStorefrontFeatured();
        }

        var inventoryItem = await db.InventoryItems
            .Where(i => i.VariantId == cmd.VariantId)
            .FirstOrDefaultAsync(ct);
        var availableStock = inventoryItem?.Available;

        return Result.Success(new VariantResponse(
            variant.Id, variant.Sku, variant.PriceOverride,
            variant.SortOrder, variant.IsActive, variant.AttributeValueIds,
            AvailableStock: availableStock));
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

        var product = await db.Products
            .Where(p => p.Id == cmd.ProductId)
            .Select(p => new { p.Slug, p.IsFeatured })
            .FirstOrDefaultAsync(ct);

        // Remove associated inventory item if it exists
        var inventoryItem = await db.InventoryItems
            .FirstOrDefaultAsync(i => i.VariantId == cmd.VariantId, ct);
        if (inventoryItem is not null)
            db.InventoryItems.Remove(inventoryItem);

        db.ProductVariants.Remove(variant);
        await db.SaveChangesAsync(ct);

        cache.InvalidateProduct(cmd.ProductId);
        if (product is not null)
        {
            cache.InvalidateStorefrontProduct(product.Slug);
            if (product.IsFeatured) cache.InvalidateStorefrontFeatured();
        }

        return Result.Success();
    }
}

/// <summary>
/// Shared attribute-value validation logic for Create and Update variant handlers.
/// </summary>
internal static class VariantAttributeHelper
{
    /// <summary>
    /// Returns a sorted list of Guid IDs to ensure attribute combinations
    /// are order-insensitive during duplicate detection.
    /// [SizeLarge, ColorBlack] == [ColorBlack, SizeLarge] after sorting.
    /// </summary>
    public static IEnumerable<Guid> SortedIds(IEnumerable<Guid> ids)
        => ids.OrderBy(id => id);

    /// <summary>
    /// Validates that every supplied attribute value ID:
    ///   1. Exists as a ProductAttributeValue.
    ///   2. Belongs to an attribute that belongs to the specified product.
    ///   3. Has no duplicate attribute (max one value per attribute).
    /// Returns an Error if validation fails, null if valid.
    /// </summary>
    public static async Task<Error?> ValidateAttributeValues(
        IApplicationDbContext db,
        Guid productId,
        List<Guid> attributeValueIds,
        Guid? excludeVariantId,
        CancellationToken ct)
    {
        // Load all attribute values for the given IDs that belong to this product's attributes
        var validValueIds = await db.ProductAttributeValues
            .Where(av =>
                attributeValueIds.Contains(av.Id) &&
                av.Attribute.ProductId == productId)
            .Select(av => new { av.Id, av.AttributeId })
            .ToListAsync(ct);

        // Check every supplied ID exists and belongs to this product
        var validSet = validValueIds.Select(v => v.Id).ToHashSet();
        var invalidId = attributeValueIds.FirstOrDefault(id => !validSet.Contains(id));
        if (invalidId != default)
            return Error.Validation("VARIANT_INVALID_ATTRIBUTE_VALUE",
                $"Attribute value '{invalidId}' is not valid for this product.");

        // Check no attribute is selected more than once (e.g. two different colors)
        var duplicateAttr = validValueIds
            .GroupBy(v => v.AttributeId)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicateAttr is not null)
            return Error.Validation("VARIANT_DUPLICATE_ATTRIBUTE",
                $"Multiple values selected for the same attribute.");

        return null;
    }

    /// <summary>
    /// Checks whether another variant on the same product already has the
    /// exact same sorted attribute-value combination.
    /// Returns an Error if a duplicate exists, null if the combination is unique.
    /// </summary>
    public static async Task<Error?> CheckDuplicateCombination(
        IApplicationDbContext db,
        Guid productId,
        Guid? excludeVariantId,
        List<Guid> attributeValueIds,
        CancellationToken ct)
    {
        // Normalise: sort the incoming IDs to produce a canonical comma-separated key
        var canonical = string.Join(",", SortedIds(attributeValueIds));

        var existing = await db.ProductVariants
            .Where(v => v.ProductId == productId && v.AttributeValueIds != null)
            .Select(v => new { v.Id, v.AttributeValueIds })
            .ToListAsync(ct);

        var duplicate = existing.FirstOrDefault(v =>
        {
            if (excludeVariantId.HasValue && v.Id == excludeVariantId.Value) return false;

            // Normalise the stored CSV by parsing, sorting, re-joining
            var storedIds = v.AttributeValueIds!
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty)
                .Where(g => g != Guid.Empty)
                .OrderBy(g => g);
            var storedCanonical = string.Join(",", storedIds);
            return storedCanonical == canonical;
        });

        if (duplicate is not null)
            return Error.Conflict("VARIANT_DUPLICATE_COMBINATION",
                "A variant with the same attribute combination already exists.");

        return null;
    }
}
