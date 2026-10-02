using KromicCommerce.Application.Features.Me.Wishlist;

namespace KromicCommerce.Application.Features.Me.Wishlist;

/// <summary>
/// Builds the wishlist card projection.
///
/// Stock is read as availability + purchasability only. The raw OnHand/Reserved columns are
/// deliberately never projected: they are an internal inventory concern, and publishing them
/// would let a client gate its UI on a number that a concurrent checkout reservation can change
/// before the customer reaches the cart. This mirrors the public storefront contract.
/// </summary>
internal static class WishlistMapper
{
    internal static WishlistItemResponse Map(
        Guid wishlistItemId,
        Guid productId,
        Guid? productVariantId,
        Product product,
        ProductVariant? variant,
        StockAvailability availability,
        bool canPurchase,
        string currencyCode,
        DateTime addedAtUtc)
        => new(
            wishlistItemId,
            productId,
            productVariantId,
            product.Name,
            product.Slug,
            PrimaryImage(product),
            product.Price,
            variant?.PriceOverride,
            // Reuses the product's own effective-price rule rather than restating it.
            product.GetEffectivePrice(variant),
            currencyCode,
            availability,
            canPurchase,
            product.Status == ProductStatus.Active,
            addedAtUtc);

    private static string? PrimaryImage(Product product) =>
        product.Images
            .OrderBy(i => i.SortOrder)
            .Where(i => i.IsPrimary)
            .Select(i => i.Asset.SecureUrl)
            .FirstOrDefault()
        ?? product.Images
            .OrderBy(i => i.SortOrder)
            .Select(i => i.Asset.SecureUrl)
            .FirstOrDefault();
}

/// <summary>
/// One inventory row, projected into memory so availability can be resolved without holding
/// tracked entities.
/// </summary>
internal sealed record InventorySnapshot(
    Guid ProductId,
    Guid? VariantId,
    int OnHand,
    int Reserved,
    int LowStockThreshold);

/// <summary>
/// Turns inventory rows into the public availability contract.
///
/// A product/variant with no inventory row is untracked and treated as purchasable, matching
/// the storefront listing. Keeping this in one place stops the wishlist from inventing a subtly
/// different definition of "can I buy this" than the product page does.
/// </summary>
internal static class StockProjection
{
    internal static (StockAvailability Availability, bool CanPurchase) Resolve(
        IReadOnlyCollection<InventorySnapshot> inventory,
        Guid productId,
        Guid? variantId)
    {
        var row = inventory.FirstOrDefault(i =>
            i.ProductId == productId && i.VariantId == variantId);

        // Untracked product: no inventory row means nothing is being counted, so it stays
        // purchasable. Same assumption the storefront listing makes.
        if (row is null)
            return (StockAvailability.InStock, true);

        var available = row.OnHand - row.Reserved;
        if (available <= 0)
            return (StockAvailability.OutOfStock, false);

        return available <= row.LowStockThreshold
            ? (StockAvailability.LowStock, true)
            : (StockAvailability.InStock, true);
    }
}

internal sealed class GetWishlistHandler(
    IApplicationDbContext db,
    IBusinessSettingsService businessSettings)
    : IQueryHandler<GetWishlistQuery, PagedResponse<WishlistItemResponse>>
{
    public async Task<Result<PagedResponse<WishlistItemResponse>>> Handle(
        GetWishlistQuery query, CancellationToken ct)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var baseQuery = db.WishlistItems
            .AsNoTracking()
            .Where(w => w.CustomerId == query.CustomerId);

        var total = await baseQuery.CountAsync(ct);

        // CreatedAtUtc, then Id: without the tiebreak two entries saved in the same tick could
        // swap places between pages and make an item appear to vanish from the list.
        var rows = await baseQuery
            .OrderByDescending(w => w.CreatedAtUtc).ThenBy(w => w.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(w => new
            {
                w.Id,
                w.ProductId,
                w.ProductVariantId,
                w.CreatedAtUtc
            })
            .ToListAsync(ct);

        if (rows.Count == 0)
            return Result.Success(new PagedResponse<WishlistItemResponse>([], page, pageSize, total));

        var currency = (await businessSettings.GetAsync(ct))?.CurrencyCode ?? "INR";

        var productIds = rows.Select(r => r.ProductId).Distinct().ToList();

        // WishlistItem has no Product navigation on purpose — a saved item has no behaviour that
        // involves the product — so the snapshot is loaded in one batched query for the page.
        var products = await db.Products
            .AsNoTracking()
            .Include(p => p.Images)
            .Include(p => p.Variants)
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        var inventory = await db.InventoryItems
            .AsNoTracking()
            .Where(i => productIds.Contains(i.ProductId))
            .Select(i => new InventorySnapshot(
                i.ProductId, i.VariantId, i.OnHand, i.Reserved, i.LowStockThreshold))
            .ToListAsync(ct);

        var responses = new List<WishlistItemResponse>(rows.Count);
        foreach (var row in rows)
        {
            // A product deleted out from under a saved entry leaves a dangling wishlist row.
            // Skip it rather than failing the whole page.
            if (!products.TryGetValue(row.ProductId, out var product)) continue;

            var variant = row.ProductVariantId.HasValue
                ? product.Variants.FirstOrDefault(v => v.Id == row.ProductVariantId.Value)
                : null;

            var (availability, canPurchase) =
                StockProjection.Resolve(inventory, row.ProductId, row.ProductVariantId);

            responses.Add(WishlistMapper.Map(
                row.Id, row.ProductId, row.ProductVariantId, product, variant,
                availability, canPurchase, currency, row.CreatedAtUtc));
        }

        return Result.Success(new PagedResponse<WishlistItemResponse>(responses, page, pageSize, total));
    }
}

internal sealed class GetWishlistStatusHandler(
    IApplicationDbContext db)
    : IQueryHandler<GetWishlistStatusQuery, WishlistStatusResponse>
{
    public async Task<Result<WishlistStatusResponse>> Handle(
        GetWishlistStatusQuery query, CancellationToken ct)
    {
        var requested = query.ProductIds.Distinct().ToList();

        var saved = await db.WishlistItems
            .AsNoTracking()
            .Where(w => w.CustomerId == query.CustomerId && requested.Contains(w.ProductId))
            .Select(w => w.ProductId)
            .Distinct()
            .ToListAsync(ct);

        return Result.Success(new WishlistStatusResponse(saved));
    }
}