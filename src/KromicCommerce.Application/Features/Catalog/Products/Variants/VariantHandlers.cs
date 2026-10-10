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

        // Automatic sort order: an explicit position wins, otherwise the variant is appended
        // after the current maximum so the admin UI never has to manage ordering on create.
        var sortOrder = cmd.SortOrder
            ?? await VariantAttributeHelper.NextSortOrderAsync(db, cmd.ProductId, ct);

        var variant = ProductVariant.Create(cmd.ProductId, cmd.Sku, cmd.PriceOverride, cmd.CompareAtPrice, sortOrder);
        if (cmd.AttributeValueIds?.Count > 0)
            variant.SetAttributeValues(VariantAttributeHelper.SortedIds(cmd.AttributeValueIds));

        db.ProductVariants.Add(variant);

        // Auto-create an InventoryItem at 0 stock so the variant appears in inventory management
        var inventoryItem = InventoryItem.Create(cmd.ProductId, variant.Id, onHand: 0);
        db.InventoryItems.Add(inventoryItem);

        // Duplicate product-level images to this variant so every combination has the same gallery
        // as the product. The UI hides product-level images when variants exist, so the variant
        // must carry its own copies. Sort order is preserved from the product image.
        var productImages = await db.ProductImages
            .Where(i => i.ProductId == cmd.ProductId && i.VariantId == null)
            .OrderBy(i => i.SortOrder)
            .ToListAsync(ct);

        foreach (var productImage in productImages)
        {
            db.ProductImages.Add(ProductImage.Create(
                cmd.ProductId,
                productImage.Asset,  // same MediaAsset reference
                productImage.SortOrder,
                productImage.IsPrimary,
                variant.Id));        // scoped to the new variant
        }

        await db.SaveChangesAsync(ct);

        var attributes = await VariantAttributeResolution.ResolveAsync(db, variant, ct);

        cache.InvalidateProductGraph(cmd.ProductId, product.Slug);

        return Result.Success(new VariantResponse(
            variant.Id, variant.Sku, variant.PriceOverride, variant.CompareAtPrice,
            variant.SortOrder, variant.IsActive, variant.AttributeValueIds,
            AvailableStock: 0,
            Attributes: attributes));
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

        variant.Update(cmd.Sku, cmd.PriceOverride, cmd.CompareAtPrice, cmd.SortOrder);
        if (cmd.AttributeValueIds?.Count > 0)
            variant.SetAttributeValues(VariantAttributeHelper.SortedIds(cmd.AttributeValueIds));
        if (cmd.IsActive) variant.Activate(); else variant.Deactivate();

        var product = await db.Products
            .Where(p => p.Id == cmd.ProductId)
            .Select(p => new { p.Slug })
            .FirstOrDefaultAsync(ct);

        await db.SaveChangesAsync(ct);

        cache.InvalidateProductGraph(cmd.ProductId, product?.Slug);

        var attributes = await VariantAttributeResolution.ResolveAsync(db, variant, ct);
        var inventoryItem = await db.InventoryItems
            .Where(i => i.VariantId == cmd.VariantId)
            .FirstOrDefaultAsync(ct);

        return Result.Success(new VariantResponse(
            variant.Id, variant.Sku, variant.PriceOverride, variant.CompareAtPrice,
            variant.SortOrder, variant.IsActive, variant.AttributeValueIds,
            AvailableStock: inventoryItem?.Available,
            Attributes: attributes));
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
            .Select(p => new { p.Slug })
            .FirstOrDefaultAsync(ct);

        // Remove associated inventory item if it exists
        var inventoryItem = await db.InventoryItems
            .FirstOrDefaultAsync(i => i.VariantId == cmd.VariantId, ct);
        if (inventoryItem is not null)
            db.InventoryItems.Remove(inventoryItem);

        // Remove variant-scoped images. Product-level images are untouched.
        var variantImages = await db.ProductImages
            .Where(i => i.VariantId == cmd.VariantId)
            .ToListAsync(ct);
        foreach (var img in variantImages)
            db.ProductImages.Remove(img);

        db.ProductVariants.Remove(variant);
        await db.SaveChangesAsync(ct);

        cache.InvalidateProductGraph(cmd.ProductId, product?.Slug);

        return Result.Success();
    }
}

/// <summary>Resolves a variant's stored attribute value IDs into display-ready name/value pairs.</summary>
internal static class VariantAttributeResolution
{
    public static async Task<IReadOnlyList<VariantAttributeValueResponse>> ResolveAsync(
        IApplicationDbContext db, ProductVariant variant, CancellationToken ct)
    {
        var ids = variant.ParsedAttributeValueIds;
        if (ids.Count == 0) return [];

        var map = await VariantAttributeHelper.ResolveAsync(db, ids, ct);

        // Preserve the variant's own stored ordering so the display order matches the
        // selection order the admin configured, not the database's arbitrary return order.
        return ids
            .Where(map.ContainsKey)
            .Select(id => map[id])
            .ToList();
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

    /// <summary>
    /// Resolves variants' stored attribute-value IDs into display-ready name/value pairs.
    ///
    /// Variants persist only the value IDs (a CSV column) so that renaming a value such as
    /// "128GB" needs no data migration. Resolution happens here, once, so every response that
    /// shows variant options resolves them identically instead of each mapper re-implementing
    /// the lookup. IDs with no matching row — a value deleted after a variant referenced it —
    /// are skipped, so a partially orphaned variant degrades to a smaller option set rather
    /// than failing the whole response.
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, VariantAttributeValueResponse>> ResolveAsync(
        IApplicationDbContext db,
        IReadOnlyCollection<Guid> attributeValueIds,
        CancellationToken ct)
    {
        var result = new Dictionary<Guid, VariantAttributeValueResponse>();
        if (attributeValueIds.Count == 0) return result;

        var rows = await db.ProductAttributeValues
            .AsNoTracking()
            .Where(av => attributeValueIds.Contains(av.Id))
            .Select(av => new { av.Id, av.AttributeId, av.Value, AttributeName = av.Attribute!.Name })
            .ToListAsync(ct);

        foreach (var row in rows)
            result[row.Id] = new VariantAttributeValueResponse(row.Id, row.AttributeId, row.AttributeName, row.Value);

        return result;
    }

    /// <summary>
    /// The sort order to give a newly created variant when the admin did not specify one.
    ///
    /// Appending after the current maximum keeps the admin's manual ordering intact and makes
    /// "create a variant" a single fieldless action, which is what removes the need for the
    /// admin UI to manage sort order at all. The first variant of a product still gets 0,
    /// so behaviour is unchanged for the common single-variant case.
    /// </summary>
    public static async Task<int> NextSortOrderAsync(
        IApplicationDbContext db, Guid productId, CancellationToken ct)
    {
        var max = await db.ProductVariants
            .Where(v => v.ProductId == productId)
            .Select(v => (int?)v.SortOrder)
            .MaxAsync(ct);
        return (max ?? -1) + 1;
    }
}
