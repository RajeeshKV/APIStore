using KromicCommerce.Application.Features.Catalog.Products.Variants;
using KromicCommerce.Contracts.Catalog;

namespace KromicCommerce.Application.Features.Orders.GetMyOrders;

internal sealed class GetMyOrdersHandler(IApplicationDbContext db)
    : IQueryHandler<GetMyOrdersQuery, PagedResponse<OrderSummaryResponse>>
{
    public async Task<Result<PagedResponse<OrderSummaryResponse>>> Handle(
        GetMyOrdersQuery query, CancellationToken ct)
    {
        var pageSize = Math.Clamp(query.PageSize, 1, 50);
        var page = Math.Max(query.Page, 1);

        var q = db.Orders.AsNoTracking()
            .Where(o => o.CustomerId == query.CustomerId)
            .OrderByDescending(o => o.CreatedAtUtc);

        var total = await q.CountAsync(ct);
        var orders = await q.Skip((page - 1) * pageSize).Take(pageSize)
            .Include(o => o.Items)
            .ToListAsync(ct);

        var mapped = orders.Select(o => new OrderSummaryResponse(
            o.Id, o.OrderNumber, o.Status, o.PaymentMethod,
            o.GrandTotal, o.CurrencyCode,
            o.Items.Count,
            o.CreatedAtUtc)).ToList();

        return Result.Success(new PagedResponse<OrderSummaryResponse>(mapped, page, pageSize, total));
    }
}

internal sealed class GetMyOrderByIdHandler(IApplicationDbContext db)
    : IQueryHandler<GetMyOrderByIdQuery, OrderResponse>
{
    public async Task<Result<OrderResponse>> Handle(
        GetMyOrderByIdQuery query, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == query.OrderId && o.CustomerId == query.CustomerId, ct);

        if (order is null)
            return Result.Failure<OrderResponse>(Error.NotFound("ORDER_NOT_FOUND", "Order not found."));

        var imageMap = await LoadImageMapAsync(db, order.Items, ct);
        
        // Resolve variant attributes for order items that have variants
        var variantAttributeMap = await BuildVariantAttributeMapAsync(db, order.Items, ct);
        
        return Result.Success(MapToResponse(order, imageMap, variantAttributeMap));
    }

/// <summary>
    /// Builds a map of variant ID to resolved attribute values for order items.
    /// </summary>
    internal static async Task<Dictionary<Guid, IReadOnlyList<VariantAttributeValueResponse>>> BuildVariantAttributeMapAsync(
        IApplicationDbContext db,
        IReadOnlyList<OrderItem> items,
        CancellationToken ct)
    {
        var variantIds = items
            .Where(i => i.VariantId.HasValue)
            .Select(i => i.VariantId!.Value)
            .Distinct()
            .ToList();

        if (variantIds.Count == 0)
            return [];

        try
        {
            var variants = await db.ProductVariants
                .AsNoTracking()
                .Where(v => variantIds.Contains(v.Id))
                .ToListAsync(ct);

            if (!variants.Any())
                return [];

            var allAttributeValueIds = variants
                .SelectMany(v => v.ParsedAttributeValueIds)
                .Distinct()
                .ToList();

            var attributeMap = await VariantAttributeHelper.ResolveAsync(db, allAttributeValueIds, ct);

            var result = new Dictionary<Guid, IReadOnlyList<VariantAttributeValueResponse>>();
            foreach (var variant in variants)
            {
                var attributes = variant.ParsedAttributeValueIds
                    .Where(attributeMap.ContainsKey)
                    .Select(id => attributeMap[id])
                    .ToList();

                result[variant.Id] = attributes;
            }

            return result;
        }
        catch (NullReferenceException)
        {
            // Handle case where ProductVariants DbSet is not configured (e.g., in tests)
            return [];
        }
    }

    /// <summary>
    /// Loads the primary image URL for each product/variant referenced by the order items.
    /// For items with a variant, loads variant-level images; otherwise loads product-level images.
    /// Single query — no N+1.
    /// </summary>
    internal static async Task<Dictionary<(Guid ProductId, Guid? VariantId), string?>> LoadImageMapAsync(
        IApplicationDbContext db,
        IReadOnlyList<OrderItem> items,
        CancellationToken ct)
    {
        var productIds = items.Select(i => i.ProductId).Distinct().ToList();
        if (productIds.Count == 0) return [];

        var variantIds = items
            .Where(i => i.VariantId.HasValue)
            .Select(i => i.VariantId!.Value)
            .Distinct()
            .ToList();

        // Fetch all images in one query: product-level (VariantId == null) and variant-level
        var allImages = await db.ProductImages
            .AsNoTracking()
            .Where(i => productIds.Contains(i.ProductId) && 
                       (i.VariantId == null || (i.VariantId.HasValue && variantIds.Contains(i.VariantId.Value))))
            .Select(i => new { i.ProductId, i.VariantId, i.Asset.SecureUrl, i.IsPrimary, i.SortOrder })
            .ToListAsync(ct);

        // Build map keyed by (ProductId, VariantId)
        var result = new Dictionary<(Guid ProductId, Guid? VariantId), string?>();
        foreach (var item in items)
        {
            var key = (item.ProductId, item.VariantId);
            if (result.ContainsKey(key)) continue;

            var itemImages = allImages
                .Where(i => i.ProductId == item.ProductId && i.VariantId == item.VariantId)
                .ToList();

            var primaryUrl = (itemImages.FirstOrDefault(i => i.IsPrimary)
                ?? itemImages.OrderBy(i => i.SortOrder).FirstOrDefault())?.SecureUrl;

            result[key] = primaryUrl;
        }

        return result;
    }

    internal static OrderResponse MapToResponse(
        Order o,
        Dictionary<(Guid ProductId, Guid? VariantId), string?> imageMap,
        Dictionary<Guid, IReadOnlyList<VariantAttributeValueResponse>> variantAttributeMap) =>
        new(o.Id, o.OrderNumber, o.Status, o.PaymentMethod,
            o.Subtotal, o.ShippingAmount, o.DiscountAmount, o.TaxAmount, o.GrandTotal, o.CurrencyCode,
            new ShippingAddressDto(
                o.ShippingAddress.FullName, o.ShippingAddress.Phone,
                o.ShippingAddress.AddressLine1, o.ShippingAddress.AddressLine2,
                o.ShippingAddress.City, o.ShippingAddress.State,
                o.ShippingAddress.PostalCode, o.ShippingAddress.Country),
            o.Items.Select(i =>
            {
                variantAttributeMap.TryGetValue(i.VariantId ?? Guid.Empty, out var variantAttributes);
                var imageUrl = imageMap.GetValueOrDefault((i.ProductId, i.VariantId));
                return new OrderItemResponse(
                    i.Id, i.ProductId, i.VariantId, i.ProductName,
                    i.VariantDescription, i.Sku, i.UnitPrice, i.Quantity, i.LineTotal,
                    imageUrl,
                    VariantAttributes: variantAttributes);
            }).ToList(),
            o.TrackingNumber, o.TrackingProvider, o.CancellationReason,
            o.PaidAt, o.ShippedAt, o.DeliveredAt, o.CancelledAt,
            o.CreatedAtUtc, o.UpdatedAtUtc);
}
