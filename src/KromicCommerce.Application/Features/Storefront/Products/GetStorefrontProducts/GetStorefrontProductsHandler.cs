namespace KromicCommerce.Application.Features.Storefront.Products.GetStorefrontProducts;

/// <summary>
/// Returns paginated, filtered, sorted storefront product listing.
///
/// All filtering, sorting, and pagination happens in PostgreSQL via EF Core IQueryable.
/// Only the required page is loaded into memory.
/// StockAvailability is derived from a LEFT JOIN on InventoryItems (null = untracked = InStock).
/// Currency comes from BusinessSettings (never hard-coded).
/// Draft and Archived products are excluded at the query level.
/// </summary>
internal sealed class GetStorefrontProductsHandler(
    IApplicationDbContext db,
    IBusinessSettingsService businessSettings,
    ILogger<GetStorefrontProductsHandler> logger)
    : IQueryHandler<GetStorefrontProductsQuery, PagedResponse<StorefrontProductSummaryResponse>>
{
    private const int MaxPageSize = 100;

    private static readonly HashSet<string> AllowedSortFields =
        ["name", "price", "created_at"];

    public async Task<Result<PagedResponse<StorefrontProductSummaryResponse>>> Handle(
        GetStorefrontProductsQuery query,
        CancellationToken cancellationToken)
    {
        var req = query.Request;
        var pageSize = Math.Clamp(req.PageSize, 1, MaxPageSize);
        var page = Math.Max(req.Page, 1);

        var settings = await businessSettings.GetAsync(cancellationToken);
        var currency = settings?.CurrencyCode ?? "INR";

        // -----------------------------------------------------------------------
        // Base query — Active products only, no tracking
        // -----------------------------------------------------------------------
        var q = db.Products
            .AsNoTracking()
            .Where(p => p.Status == ProductStatus.Active);

        // -----------------------------------------------------------------------
        // Filters
        // -----------------------------------------------------------------------
        if (!string.IsNullOrWhiteSpace(req.Search))
        {
            var term = req.Search.Trim().ToLower();
            q = q.Where(p =>
                p.Name.ToLower().Contains(term) ||
                (p.ShortDescription != null && p.ShortDescription.ToLower().Contains(term)) ||
                (p.Sku != null && p.Sku.ToLower().Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(req.CategorySlug))
        {
            var slug = req.CategorySlug.Trim().ToLower();
            q = q.Where(p => p.Category != null && p.Category.Slug == slug && p.Category.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(req.BrandSlug))
        {
            var slug = req.BrandSlug.Trim().ToLower();
            q = q.Where(p => p.Brand != null && p.Brand.Slug == slug && p.Brand.IsActive);
        }

        if (req.MinPrice.HasValue) q = q.Where(p => p.Price >= req.MinPrice.Value);
        if (req.MaxPrice.HasValue) q = q.Where(p => p.Price <= req.MaxPrice.Value);
        if (req.IsFeatured.HasValue) q = q.Where(p => p.IsFeatured == req.IsFeatured.Value);

        // Attribute filters — AND between different attribute names, OR within same name
        if (req.AttributeFilters is { Count: > 0 })
        {
            var grouped = req.AttributeFilters
                .GroupBy(f => f.AttributeName.Trim().ToLower())
                .ToList();

            foreach (var group in grouped)
            {
                var attrName = group.Key;
                var values = group.Select(f => f.AttributeValue.Trim().ToLower()).ToList();

                // Subquery: product has an attribute with this name containing at least one of the values
                q = q.Where(p =>
                    p.Attributes.Any(a =>
                        a.Name.ToLower() == attrName &&
                        a.Values.Any(v => values.Contains(v.Value.ToLower()))));
            }
        }

        // -----------------------------------------------------------------------
        // InStockOnly — LEFT JOIN on base-product InventoryItem (VariantId == null)
        // -----------------------------------------------------------------------
        if (req.InStockOnly)
        {
            q = q.Where(p =>
                !db.InventoryItems.Any(inv =>
                    inv.ProductId == p.Id &&
                    inv.VariantId == null &&
                    (inv.OnHand - inv.Reserved) <= 0));
        }

        // -----------------------------------------------------------------------
        // Count before paging
        // -----------------------------------------------------------------------
        var total = await q.CountAsync(cancellationToken);

        // -----------------------------------------------------------------------
        // Sort (whitelisted, deterministic default: CreatedAtUtc DESC)
        // -----------------------------------------------------------------------
        var desc = !string.Equals(req.SortDirection, "asc", StringComparison.OrdinalIgnoreCase);
        q = req.SortBy?.ToLowerInvariant() switch
        {
            "price" => desc ? q.OrderByDescending(p => p.Price) : q.OrderBy(p => p.Price),
            "name"  => desc ? q.OrderByDescending(p => p.Name) : q.OrderBy(p => p.Name),
            _       => desc ? q.OrderByDescending(p => p.CreatedAtUtc) : q.OrderBy(p => p.CreatedAtUtc)
        };

        // -----------------------------------------------------------------------
        // Pagination + projection — only needed columns loaded
        // -----------------------------------------------------------------------
        var products = await q
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
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
            // Reconstruct availability without loading the full entity
            if (p.Inventory is not null)
            {
                // Use projected values directly to avoid extra allocation
                var available = p.Inventory.OnHand - p.Inventory.Reserved;
                var isOut = available <= 0;
                var isLow = available > 0 && available <= p.Inventory.LowStockThreshold;
                var availability = isOut
                    ? StockAvailability.OutOfStock
                    : (isLow ? StockAvailability.LowStock : StockAvailability.InStock);

                return new StorefrontProductSummaryResponse(
                    p.Id, p.Name, p.Slug, p.ShortDescription,
                    p.Price, p.CompareAtPrice, currency,
                    p.PrimaryImageUrl,
                    availability, CanPurchase: !isOut,
                    p.CategoryId, p.CategoryName, p.CategorySlug,
                    p.BrandId, p.BrandName, p.BrandSlug,
                    p.IsFeatured);
            }

            // No inventory record → untracked, assumed in stock
            return new StorefrontProductSummaryResponse(
                p.Id, p.Name, p.Slug, p.ShortDescription,
                p.Price, p.CompareAtPrice, currency,
                p.PrimaryImageUrl,
                StockAvailability.InStock, CanPurchase: true,
                p.CategoryId, p.CategoryName, p.CategorySlug,
                p.BrandId, p.BrandName, p.BrandSlug,
                p.IsFeatured);
        }).ToList();

        logger.LogDebug("Storefront product listing: page={Page} pageSize={PageSize} total={Total}",
            page, pageSize, total);

        return Result.Success(new PagedResponse<StorefrontProductSummaryResponse>(
            items, page, pageSize, total));
    }
}
