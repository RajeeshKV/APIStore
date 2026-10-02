using KromicCommerce.Application.Abstractions.Catalog;

namespace KromicCommerce.Application.Features.Catalog.Inventory;

internal sealed class SetStockHandler(
    IApplicationDbContext db,
    ICatalogCacheService cache,
    ILogger<SetStockHandler> logger)
    : ICommandHandler<SetStockCommand, InventoryResponse>
{
    public async Task<Result<InventoryResponse>> Handle(
        SetStockCommand command, CancellationToken cancellationToken)
    {
        var productSlug = await db.Products
            .Where(p => p.Id == command.ProductId)
            .Select(p => p.Slug)
            .FirstOrDefaultAsync(cancellationToken);

        if (productSlug is null)
            return Result.Failure<InventoryResponse>(
                Error.NotFound("PRODUCT_NOT_FOUND", "Product not found."));

        var inventory = await db.InventoryItems
            .FirstOrDefaultAsync(i =>
                i.ProductId == command.ProductId && i.VariantId == command.VariantId,
                cancellationToken);

        if (inventory is null)
        {
            // A base-product row (no variant) must not be created for a product that already has
            // variants, because availability is summed across a product's inventory rows and the
            // two would be counted together, overstating stock. The database enforces this with a
            // trigger, but a trigger violation would surface as an unhandled 500, so it is
            // rejected here with an actionable message instead.
            if (command.VariantId is null)
            {
                var hasVariantRows = await db.InventoryItems
                    .AnyAsync(i =>
                        i.ProductId == command.ProductId && i.VariantId != null,
                        cancellationToken);

                if (hasVariantRows)
                    return Result.Failure<InventoryResponse>(
                        Error.Conflict(
                            "INVENTORY_VARIANT_ROWS_EXIST",
                            "This product tracks stock per variant. Set stock against a specific " +
                            "variant instead of the product."));
            }

            inventory = InventoryItem.Create(
                command.ProductId, command.VariantId,
                command.OnHand, command.LowStockThreshold);
            db.InventoryItems.Add(inventory);
        }
        else
        {
            inventory.SetOnHand(command.OnHand);
            inventory.SetLowStockThreshold(command.LowStockThreshold);
        }

        await db.SaveChangesAsync(cancellationToken);

        // One graph call, not individual keys. The stock graph covers both projections that embed
        // availability (the storefront product page and the featured list); counts are unaffected
        // by a stock change, so the brand/category ProductCount lists stay valid.
        cache.InvalidateStockGraph(productSlug);

        if (inventory.IsLowStock)
            logger.LogWarning("Low stock detected. ProductId: {ProductId}, Available: {Available}",
                command.ProductId, inventory.Available);

        return Result.Success(MapToResponse(inventory));
    }

    internal static InventoryResponse MapToResponse(InventoryItem i) =>
        new(i.Id, i.ProductId, i.VariantId, i.OnHand, i.Reserved, i.Available,
            i.LowStockThreshold, i.IsLowStock, i.IsOutOfStock, i.UpdatedAt);
}

internal sealed class AdjustStockHandler(
    IApplicationDbContext db,
    ICatalogCacheService cache,
    ILogger<AdjustStockHandler> logger)
    : ICommandHandler<AdjustStockCommand, InventoryResponse>
{
    public async Task<Result<InventoryResponse>> Handle(
        AdjustStockCommand command, CancellationToken cancellationToken)
    {
        var inventory = await db.InventoryItems
            .FirstOrDefaultAsync(i =>
                i.ProductId == command.ProductId && i.VariantId == command.VariantId,
                cancellationToken);

        if (inventory is null)
            return Result.Failure<InventoryResponse>(
                Error.NotFound("INVENTORY_NOT_FOUND",
                    "Inventory record not found. Use SetStock to initialise it first."));

        try
        {
            inventory.AdjustOnHand(command.Delta);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure<InventoryResponse>(
                Error.Conflict("STOCK_ADJUSTMENT_INVALID", ex.Message));
        }

        var productSlug = await db.Products
            .Where(p => p.Id == command.ProductId)
            .Select(p => p.Slug)
            .FirstOrDefaultAsync(cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        cache.InvalidateStockGraph(productSlug);

        logger.LogInformation(
            "Stock adjusted. ProductId: {ProductId} Delta: {Delta} Reason: {Reason}",
            command.ProductId, command.Delta, command.Reason ?? "none");

        return Result.Success(SetStockHandler.MapToResponse(inventory));
    }
}
