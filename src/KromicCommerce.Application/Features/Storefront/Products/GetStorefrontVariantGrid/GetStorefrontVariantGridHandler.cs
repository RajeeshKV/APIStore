using KromicCommerce.Application.Features.Catalog.Products.Variants;
using KromicCommerce.Contracts.Catalog;

namespace KromicCommerce.Application.Features.Storefront.Products.GetStorefrontVariantGrid;

/// <summary>
/// Returns one row per active variant of an active product, so the storefront can render
/// each combination (e.g. "Blue-256GB", "Red-256GB") as a separate card in the product grid.
///
/// Filtering, sorting and pagination happen at the variant level. Product-level filters
/// (category, brand, featured, search) are pushed down into the product subquery so they
/// still use indexes. Attribute filters are applied per-product by checking that the
/// product's variant set covers the requested attribute values.
///
/// A product with no variants appears once with VariantId null so the grid never drops
/// variant-less products.
/// </summary>
internal sealed class GetStorefrontVariantGridHandler(
    IApplicationDbContext db,
    IBusinessSettingsService businessSettings,
    ILogger<GetStorefrontVariantGridHandler> logger)
    : IQueryHandler<GetStorefrontVariantGridQuery, PagedResponse<StorefrontVariantRowResponse>>
{
    private const int MaxPageSize = 100;

    private static readonly HashSet<string> AllowedSortFields =
        ["name", "price", "created_at"];

    public async Task<Result<PagedResponse<StorefrontVariantRowResponse>>> Handle(
        GetStorefrontVariantGridQuery query,
        CancellationToken cancellationToken)
    {
        var req = query.Request;
        var pageSize = Math.Clamp(req.PageSize, 1, MaxPageSize);
        var page = Math.Max(req.Page, 1);

        var settings = await businessSettings.GetAsync(cancellationToken);
        var currency = settings?.CurrencyCode ?? "INR";

        // -----------------------------------------------------------------------
        // Step 1 — build the product subquery (shared filter logic)
        // -----------------------------------------------------------------------
        var productQ = db.Products.AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Where(p => p.Status == ProductStatus.Active);

        if (!string.IsNullOrWhiteSpace(req.Search))
        {
            var term = req.Search.Trim().ToLower();
            productQ = productQ.Where(p =>
                p.Name.ToLower().Contains(term) ||
                (p.ShortDescription != null && p.ShortDescription.ToLower().Contains(term)) ||
                (p.Sku != null && p.Sku.ToLower().Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(req.CategorySlug))
        {
            var slug = req.CategorySlug.Trim().ToLower();
            productQ = productQ.Where(p =>
                p.Category != null && p.Category.Slug == slug && p.Category.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(req.BrandSlug))
        {
            var slug = req.BrandSlug.Trim().ToLower();
            productQ = productQ.Where(p =>
                p.Brand != null && p.Brand.Slug == slug && p.Brand.IsActive);
        }

        if (req.MinPrice.HasValue) productQ = productQ.Where(p => p.Price >= req.MinPrice.Value);
        if (req.MaxPrice.HasValue) productQ = productQ.Where(p => p.Price <= req.MaxPrice.Value);
        if (req.IsFeatured.HasValue)
            productQ = productQ.Where(p => p.IsFeatured == req.IsFeatured.Value);

        // Attribute filters: product must have at least one variant covering each attribute group
        if (req.AttributeFilters is { Count: > 0 })
        {
            var grouped = req.AttributeFilters
                .GroupBy(f => f.AttributeName.Trim().ToLower())
                .ToList();

            foreach (var group in grouped)
            {
                var attrName = group.Key;
                var values = group.Select(f => f.AttributeValue.Trim().ToLower()).ToList();

                productQ = productQ.Where(p =>
                    p.Attributes.Any(a =>
                        a.Name.ToLower() == attrName &&
                        a.Values.Any(v => values.Contains(v.Value.ToLower()))));
            }
        }

        // Materialise the matching product ids once — all variant filtering is scoped to them.
        var matchingProductIds = await productQ
            .OrderByDescending(p => p.CreatedAtUtc)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        if (matchingProductIds.Count == 0)
            return Result.Success(new PagedResponse<StorefrontVariantRowResponse>([], page, pageSize, 0));

        // -----------------------------------------------------------------------
        // Step 2 — build variant rows for matching products
        // -----------------------------------------------------------------------
        var variantBase = db.ProductVariants.AsNoTracking()
            .Where(v => matchingProductIds.Contains(v.ProductId) && v.IsActive);

        // In-stock filter: keep variants whose effective stock is purchasable.
        if (req.InStockOnly)
        {
            variantBase = variantBase.Where(v =>
                db.InventoryItems.Any(inv =>
                    inv.ProductId == v.ProductId && inv.VariantId == v.Id
                    && (inv.OnHand - inv.Reserved) > 0));
        }

        var totalVariants = await variantBase.CountAsync(cancellationToken);

        // -----------------------------------------------------------------------
        // Sort
        // -----------------------------------------------------------------------
        var desc = !string.Equals(req.SortDirection, "asc", StringComparison.OrdinalIgnoreCase);
        variantBase = req.SortBy?.ToLowerInvariant() switch
        {
            "price" => desc
                ? variantBase.OrderByDescending(v => v.PriceOverride).ThenByDescending(v => v.Product.Price)
                : variantBase.OrderBy(v => v.PriceOverride).ThenBy(v => v.Product.Price),
            "name" => desc
                ? variantBase.OrderByDescending(v => v.Product.Name).ThenByDescending(v => v.Id)
                : variantBase.OrderBy(v => v.Product.Name).ThenBy(v => v.Id),
            _ => desc
                ? variantBase.OrderByDescending(v => v.CreatedAtUtc).ThenByDescending(v => v.Id)
                : variantBase.OrderBy(v => v.CreatedAtUtc).ThenBy(v => v.Id)
        };

// -----------------------------------------------------------------------
        // Page: load variant rows with product, inventory and images
        // -----------------------------------------------------------------------
        var variantRows = await variantBase
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(v => new
            {
                v.Id,
                VariantId = (Guid?)v.Id,
                v.ProductId,
                ProductName = v.Product.Name,
                Slug = v.Product.Slug,
                v.Sku,
                v.PriceOverride,
                BasePrice = v.Product.Price,
                v.IsActive,
                ProductPrice = v.Product.Price,
                ProductCompareAtPrice = v.Product.CompareAtPrice,
                PrimaryImageUrl = db.ProductImages
                    .Where(i => i.ProductId == v.ProductId && i.VariantId == v.Id)
                    .OrderBy(i => i.SortOrder)
                    .Select(i => i.Asset.SecureUrl)
                    .FirstOrDefault(),
                CategoryId = v.Product.CategoryId,
                CategoryName = v.Product.Category != null ? v.Product.Category.Name : null,
                CategorySlug = v.Product.Category != null ? v.Product.Category.Slug : null,
                BrandId = v.Product.BrandId,
                BrandName = v.Product.Brand != null ? v.Product.Brand.Name : null,
                BrandSlug = v.Product.Brand != null ? v.Product.Brand.Slug : null,
                v.Product.IsFeatured,
                RatingAverage = v.Product.RatingAverage,
                RatingCount = v.Product.RatingCount,
                InventoryOnHand = db.InventoryItems
                    .Where(i => i.ProductId == v.ProductId && i.VariantId == v.Id)
                    .Select(i => (int?)i.OnHand)
                    .FirstOrDefault() ?? 0,
                InventoryReserved = db.InventoryItems
                    .Where(i => i.ProductId == v.ProductId && i.VariantId == v.Id)
                    .Select(i => (int?)i.Reserved)
                    .FirstOrDefault() ?? 0,
                LowStockThreshold = db.InventoryItems
                    .Where(i => i.ProductId == v.ProductId && i.VariantId == v.Id)
                    .Select(i => (int?)i.LowStockThreshold)
                    .FirstOrDefault() ?? 5,
                VariantImages = v.Images
                    .OrderBy(i => i.SortOrder)
                    .Select(i => new StorefrontImageResponse(
                        i.Id, i.Asset.SecureUrl, i.Asset.AltText, i.SortOrder, i.IsPrimary))
                    .ToList(),
                VariantAttributeValueIds = v.AttributeValueIds
            })
.ToListAsync(cancellationToken);

        // Resolve variant attributes for all variants in the grid
        var variantAttributeMap = variantRows.Count > 0
            ? await VariantAttributeHelper.ResolveAsync(
                db, variantRows
                    .Where(v => !string.IsNullOrWhiteSpace(v.VariantAttributeValueIds))
                    .SelectMany(v => v.VariantAttributeValueIds!.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    .Select(s => s.Trim())
                    .Where(s => Guid.TryParse(s, out _))
                    .Select(Guid.Parse)
                    .Distinct()
                    .ToList(), cancellationToken)
            : new Dictionary<Guid, VariantAttributeValueResponse>();

        // -----------------------------------------------------------------------
        // Products without variants: one fallback row each so the grid never drops them.
        // -----------------------------------------------------------------------
        var productsWithVariants = await variantBase.Select(v => v.ProductId).Distinct().ToListAsync(cancellationToken);
        var productsWithoutVariants = matchingProductIds.Except(productsWithVariants).ToList();

        // Apply InStockOnly filter to fallback products too
        if (req.InStockOnly)
        {
            var productIdsWithStock = await db.InventoryItems
                .Where(i => productsWithoutVariants.Contains(i.ProductId) && i.VariantId == null
                    && (i.OnHand - i.Reserved) > 0)
                .Select(i => i.ProductId)
                .Distinct()
                .ToListAsync(cancellationToken);
            productsWithoutVariants = productsWithoutVariants.Intersect(productIdsWithStock).ToList();
        }

        List<StorefrontVariantRowResponse> items;

        if (productsWithoutVariants.Count > 0)
        {
            var fallbackRows = await db.Products.AsNoTracking()
                .Where(p => productsWithoutVariants.Contains(p.Id))
                .Select(p => new
                {
                    p.Id,
                    VariantId = (Guid?)null,
                    p.Name,
                    Slug = p.Slug,
                    Sku = (string?)null,
                    PriceOverride = (decimal?)null,
                    BasePrice = p.Price,
                    IsActive = true,
                    ProductPrice = p.Price,
                    ProductCompareAtPrice = p.CompareAtPrice,
                    PrimaryImageUrl = db.ProductImages
                        .Where(i => i.ProductId == p.Id && i.VariantId == null)
                        .OrderBy(i => i.SortOrder)
                        .Select(i => i.Asset.SecureUrl)
                        .FirstOrDefault()
                        ?? db.ProductImages
                            .Where(i => i.ProductId == p.Id)
                            .OrderBy(i => i.SortOrder)
                            .Select(i => i.Asset.SecureUrl)
                            .FirstOrDefault(),
                    CategoryId = p.CategoryId,
                    CategoryName = p.Category != null ? p.Category.Name : null,
                    CategorySlug = p.Category != null ? p.Category.Slug : null,
                    BrandId = p.BrandId,
                    BrandName = p.Brand != null ? p.Brand.Name : null,
                    BrandSlug = p.Brand != null ? p.Brand.Slug : null,
                    p.IsFeatured,
                    RatingAverage = p.RatingAverage,
                    RatingCount = p.RatingCount,
                    InventoryOnHand = db.InventoryItems
                        .Where(i => i.ProductId == p.Id && i.VariantId == null)
                        .Select(i => (int?)i.OnHand)
                        .FirstOrDefault() ?? 0,
                    InventoryReserved = db.InventoryItems
                        .Where(i => i.ProductId == p.Id && i.VariantId == null)
                        .Select(i => (int?)i.Reserved)
                        .FirstOrDefault() ?? 0,
                    LowStockThreshold = db.InventoryItems
                        .Where(i => i.ProductId == p.Id && i.VariantId == null)
                        .Select(i => (int?)i.LowStockThreshold)
                        .FirstOrDefault() ?? 5,
                    VariantImages = new List<StorefrontImageResponse>()
                })
                .ToListAsync(cancellationToken);

            var fallbackItems = fallbackRows.Select(v =>
            {
                var effectivePrice = v.PriceOverride ?? v.BasePrice;
                var available = v.InventoryOnHand - v.InventoryReserved;
                var isOut = available <= 0;
                var isLow = available > 0 && available <= v.LowStockThreshold;
                var availability = isOut
                    ? StockAvailability.OutOfStock
                    : (isLow ? StockAvailability.LowStock : StockAvailability.InStock);

                return new StorefrontVariantRowResponse(
                    v.Id, v.VariantId, v.Id, v.Slug, v.Name, v.Sku,
                    effectivePrice, v.ProductCompareAtPrice, currency, v.PrimaryImageUrl,
                    availability, v.IsActive && !isOut,
                    v.CategoryId, v.CategoryName, v.CategorySlug,
                    v.BrandId, v.BrandName, v.BrandSlug,
                    v.IsFeatured, v.RatingAverage, v.RatingCount,
                    VariantAttributes: null,
                    v.VariantImages);
            }).ToList();

            var variantItems = variantRows.Select(v =>
            {
                var effectivePrice = v.PriceOverride ?? v.BasePrice;
                var available = v.InventoryOnHand - v.InventoryReserved;
                var isOut = available <= 0;
                var isLow = available > 0 && available <= v.LowStockThreshold;
                var availability = isOut
                    ? StockAvailability.OutOfStock
                    : (isLow ? StockAvailability.LowStock : StockAvailability.InStock);

                // Resolve variant attributes
                IReadOnlyList<VariantAttributeValueResponse>? variantAttributes = null;
                if (!string.IsNullOrWhiteSpace(v.VariantAttributeValueIds))
                {
                    var attrIds = v.VariantAttributeValueIds
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim())
                        .Where(s => Guid.TryParse(s, out _))
                        .Select(Guid.Parse)
                        .ToList();

                    variantAttributes = attrIds
                        .Where(variantAttributeMap.ContainsKey)
                        .Select(id => variantAttributeMap[id])
                        .ToList();
                }

                // CompareAtPrice: product CompareAtPrice (variants don't have separate compare-at price)
                var compareAtPrice = v.ProductCompareAtPrice;

                return new StorefrontVariantRowResponse(
                    v.Id, v.VariantId, v.ProductId, v.Slug, v.ProductName, v.Sku,
                    effectivePrice, compareAtPrice, currency, v.PrimaryImageUrl,
                    availability, v.IsActive && !isOut,
                    v.CategoryId, v.CategoryName, v.CategorySlug,
                    v.BrandId, v.BrandName, v.BrandSlug,
                    v.IsFeatured, v.RatingAverage, v.RatingCount,
                    variantAttributes,
                    v.VariantImages);
            }).ToList();

            // Merge, re-sort by the same sort field, and re-apply the page window.
            var merged = variantItems.Concat(fallbackItems).ToList();
            merged = ApplySort(merged, req.SortBy, desc);
            var paged = merged.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            items = paged;
        }
        else
        {
            items = variantRows.Select(v =>
            {
                var effectivePrice = v.PriceOverride ?? v.BasePrice;
                var available = v.InventoryOnHand - v.InventoryReserved;
                var isOut = available <= 0;
                var isLow = available > 0 && available <= v.LowStockThreshold;
                var availability = isOut
                    ? StockAvailability.OutOfStock
                    : (isLow ? StockAvailability.LowStock : StockAvailability.InStock);

                // Resolve variant attributes
                IReadOnlyList<VariantAttributeValueResponse>? variantAttributes = null;
                if (!string.IsNullOrWhiteSpace(v.VariantAttributeValueIds))
                {
                    var attrIds = v.VariantAttributeValueIds
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim())
                        .Where(s => Guid.TryParse(s, out _))
                        .Select(Guid.Parse)
                        .ToList();

                    variantAttributes = attrIds
                        .Where(variantAttributeMap.ContainsKey)
                        .Select(id => variantAttributeMap[id])
                        .ToList();
                }

                // CompareAtPrice: product CompareAtPrice (variants don't have separate compare-at price)
                var compareAtPrice = v.ProductCompareAtPrice;

                return new StorefrontVariantRowResponse(
                    v.Id, v.VariantId, v.ProductId, v.Slug, v.ProductName, v.Sku,
                    effectivePrice, compareAtPrice, currency, v.PrimaryImageUrl,
                    availability, v.IsActive && !isOut,
                    v.CategoryId, v.CategoryName, v.CategorySlug,
                    v.BrandId, v.BrandName, v.BrandSlug,
                    v.IsFeatured, v.RatingAverage, v.RatingCount,
                    variantAttributes,
                    v.VariantImages);
            }).ToList();
        }

        var total = totalVariants + (productsWithoutVariants.Count > 0 ? productsWithoutVariants.Count : 0);

        logger.LogDebug(
            "Storefront variant grid: page={Page} pageSize={PageSize} total={Total} items={Items}",
            page, pageSize, total, items.Count);

        return Result.Success(new PagedResponse<StorefrontVariantRowResponse>(
            items, page, pageSize, total));
    }

    private static List<StorefrontVariantRowResponse> ApplySort(
        List<StorefrontVariantRowResponse> items, string? sortBy, bool desc)
    {
        return sortBy?.ToLowerInvariant() switch
        {
            "price" => desc
                ? items.OrderByDescending(i => i.EffectivePrice).ThenByDescending(i => i.Name).ToList()
                : items.OrderBy(i => i.EffectivePrice).ThenBy(i => i.Name).ToList(),
            "name" => desc
                ? items.OrderByDescending(i => i.Name).ThenByDescending(i => i.Id).ToList()
                : items.OrderBy(i => i.Name).ThenBy(i => i.Id).ToList(),
            _ => desc
                ? items.OrderByDescending(i => i.Id).ToList()
                : items.OrderBy(i => i.Id).ToList()
        };
    }
}


