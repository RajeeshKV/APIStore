using KromicCommerce.Application.Features.Me.Wishlist;

namespace KromicCommerce.Application.Features.Me.Wishlist;

internal sealed class AddWishlistItemHandler(
    IApplicationDbContext db,
    IBusinessSettingsService businessSettings)
    : ICommandHandler<AddWishlistItemCommand, AddWishlistItemResponse>
{
    public async Task<Result<AddWishlistItemResponse>> Handle(
        AddWishlistItemCommand cmd, CancellationToken ct)
    {
        var product = await db.Products
            .AsNoTracking()
            .Include(p => p.Images)
            .Include(p => p.Variants)
            .FirstOrDefaultAsync(p => p.Id == cmd.ProductId, ct);

        if (product is null)
            return Result.Failure<AddWishlistItemResponse>(
                Error.NotFound("PRODUCT_NOT_FOUND", "Product not found."));

        // Only live products can be saved. Otherwise a customer's wishlist fills with entries
        // that can never be bought and no page can explain why.
        if (product.Status != ProductStatus.Active)
            return Result.Failure<AddWishlistItemResponse>(
                Error.Validation("PRODUCT_NOT_AVAILABLE", "This product is not available."));

        ProductVariant? variant = null;
        if (cmd.ProductVariantId.HasValue)
        {
            variant = product.Variants.FirstOrDefault(v => v.Id == cmd.ProductVariantId.Value);
            if (variant is null)
                return Result.Failure<AddWishlistItemResponse>(
                    Error.Validation("PRODUCT_VARIANT_MISMATCH",
                        "The selected variant does not belong to this product."));
            if (!variant.IsActive)
                return Result.Failure<AddWishlistItemResponse>(
                    Error.Validation("PRODUCT_VARIANT_UNAVAILABLE", "The selected variant is not available."));
        }

        // Atomic insert; the unique index (NULLS NOT DISTINCT) decides the winner under
        // concurrency. No existence pre-check — a read-then-write here would still race.
        var created = await db.TryAddWishlistItemAsync(
            cmd.CustomerId, cmd.ProductId, cmd.ProductVariantId, ct);

        // Whether this request inserted the row or lost the race, the row now exists and is the
        // customer's — so the response describes a real entry rather than a claimed one.
        var item = await db.WishlistItems
            .AsNoTracking()
            .FirstAsync(w =>
                w.CustomerId == cmd.CustomerId &&
                w.ProductId == cmd.ProductId &&
                w.ProductVariantId == cmd.ProductVariantId, ct);

        return await BuildAsync(cmd, product, variant, item, created, ct);
    }

    private async Task<Result<AddWishlistItemResponse>> BuildAsync(
        AddWishlistItemCommand cmd,
        Product product,
        ProductVariant? variant,
        WishlistItem item,
        bool created,
        CancellationToken ct)
    {
        var currency = (await businessSettings.GetAsync(ct))?.CurrencyCode ?? "INR";

        var inventory = await db.InventoryItems
            .AsNoTracking()
            .Where(i => i.ProductId == cmd.ProductId && i.VariantId == cmd.ProductVariantId)
            .Select(i => new InventorySnapshot(
                i.ProductId, i.VariantId, i.OnHand, i.Reserved, i.LowStockThreshold))
            .FirstOrDefaultAsync(ct);

        var (availability, canPurchase) =
            StockProjection.Resolve(inventory is null ? [] : [inventory], cmd.ProductId, cmd.ProductVariantId);

        return Result.Success(new AddWishlistItemResponse(
            WishlistMapper.Map(
                item.Id, item.ProductId, item.ProductVariantId, product, variant,
                availability, canPurchase, currency, item.CreatedAtUtc),
            created));
    }
}

internal sealed class RemoveWishlistItemHandler(
    IApplicationDbContext db)
    : ICommandHandler<RemoveWishlistItemCommand>
{
    public async Task<Result> Handle(RemoveWishlistItemCommand cmd, CancellationToken ct)
    {
        // Scoped to the caller. Without CustomerId in the predicate one customer could delete
        // another's saved item by guessing its id.
        var item = await db.WishlistItems
            .FirstOrDefaultAsync(w =>
                w.CustomerId == cmd.CustomerId &&
                w.ProductId == cmd.ProductId &&
                w.ProductVariantId == cmd.ProductVariantId, ct);

        if (item is null)
            return Result.Failure(
                Error.NotFound("WISHLIST_ITEM_NOT_FOUND", "That item is not on your wishlist."));

        item.RaiseRemoved();
        db.WishlistItems.Remove(item);
        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}

internal sealed class ClearWishlistHandler(
    IApplicationDbContext db,
    ILogger<ClearWishlistHandler> logger)
    : ICommandHandler<ClearWishlistCommand>
{
    public async Task<Result> Handle(ClearWishlistCommand cmd, CancellationToken ct)
    {
        // Scoped to the caller: "clear my wishlist" must never touch anyone else's rows.
        var removed = await db.WishlistItems
            .Where(w => w.CustomerId == cmd.CustomerId)
            .ToListAsync(ct);

        db.WishlistItems.RemoveRange(removed);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Wishlist cleared for customer {CustomerId}: {Count} item(s) removed",
            cmd.CustomerId, removed);

        return Result.Success();
    }
}