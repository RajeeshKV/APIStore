namespace KromicCommerce.Application.Features.Storefront.Products.GetRelatedProducts;

/// <summary>
/// Returns related products for a storefront product detail page.
/// Logic: same category → prefer same brand → exclude current → active only → limit.
/// All filtering is done in PostgreSQL.
/// </summary>
internal sealed class GetRelatedProductsHandler(
    IApplicationDbContext db,
    IBusinessSettingsService businessSettings)
    : IQueryHandler<GetRelatedProductsQuery, IReadOnlyList<StorefrontProductSummaryResponse>>
{
    private const int MaxLimit = 20;

    public async Task<Result<IReadOnlyList<StorefrontProductSummaryResponse>>> Handle(
        GetRelatedProductsQuery query,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, MaxLimit);

        var settings = await businessSettings.GetAsync(cancellationToken);
        var currency = settings?.CurrencyCode ?? "INR";

        // Base: same category, active, not the current product
        var q = db.Products
            .AsNoTracking()
            .Where(p =>
                p.Status == ProductStatus.Active &&
                p.Id != query.ProductId &&
                p.CategoryId == query.CategoryId);

        // Prefer same brand — order brand match first, then by created date
        var products = await q
            .OrderByDescending(p => p.BrandId == query.BrandId)
            .ThenByDescending(p => p.CreatedAtUtc)
            .Take(limit)
            .Select(p => new
            {
                p.Id, p.Name, p.Slug, p.ShortDescription,
                p.Price, p.CompareAtPrice, p.IsFeatured,
                p.CategoryId,
                CategoryName = p.Category != null ? p.Category.Name : null,
                CategorySlug = p.Category != null ? p.Category.Slug : null,
                p.BrandId,
                BrandName = p.Brand != null ? p.Brand.Name : null,
                BrandSlug = p.Brand != null ? p.Brand.Slug : null,
                PrimaryImageUrl = p.Images
                    .OrderBy(i => i.SortOrder)
                    .Where(i => i.IsPrimary)
                    .Select(i => i.Asset.SecureUrl)
                    .FirstOrDefault()
                    ?? p.Images.OrderBy(i => i.SortOrder).Select(i => i.Asset.SecureUrl).FirstOrDefault(),
                Inventory = db.InventoryItems
                    .Where(inv => inv.ProductId == p.Id && inv.VariantId == null)
                    .Select(inv => new { inv.OnHand, inv.Reserved, inv.LowStockThreshold })
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var items = products.Select(p =>
        {
            StockAvailability availability;
            bool canPurchase;
            if (p.Inventory is null)
            {
                availability = StockAvailability.InStock;
                canPurchase = true;
            }
            else
            {
                var avail = p.Inventory.OnHand - p.Inventory.Reserved;
                availability = avail <= 0 ? StockAvailability.OutOfStock
                    : (avail <= p.Inventory.LowStockThreshold ? StockAvailability.LowStock
                        : StockAvailability.InStock);
                canPurchase = avail > 0;
            }

            return new StorefrontProductSummaryResponse(
                p.Id, p.Name, p.Slug, p.ShortDescription,
                p.Price, p.CompareAtPrice, currency,
                p.PrimaryImageUrl, availability, canPurchase,
                p.CategoryId, p.CategoryName, p.CategorySlug,
                p.BrandId, p.BrandName, p.BrandSlug,
                p.IsFeatured);
        }).ToList();

        return Result.Success<IReadOnlyList<StorefrontProductSummaryResponse>>(items);
    }
}
