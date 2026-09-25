using KromicCommerce.Application.Features.Catalog.Products;

namespace KromicCommerce.Application.Features.Catalog.Products.GetProducts;

/// <summary>
/// Whitelisted sort fields — prevents SQL injection through arbitrary ORDER BY.
/// </summary>
internal static class ProductSortFields
{
    private static readonly HashSet<string> Allowed =
        ["name", "price", "created_at", "updated_at"];

    public static bool IsValid(string? field) =>
        field is null || Allowed.Contains(field.ToLowerInvariant());
}

internal sealed class GetProductsHandler(IApplicationDbContext db)
    : IQueryHandler<GetProductsQuery, PagedResponse<ProductSummaryResponse>>
{
    private const int MaxPageSize = 100;

    public async Task<Result<PagedResponse<ProductSummaryResponse>>> Handle(
        GetProductsQuery query, CancellationToken cancellationToken)
    {
        var req = query.Request;

        if (!ProductSortFields.IsValid(req.SortBy))
            return Result.Failure<PagedResponse<ProductSummaryResponse>>(
                Error.Validation("INVALID_SORT_FIELD", $"Invalid sort field: {req.SortBy}."));

        var pageSize = Math.Clamp(req.PageSize, 1, MaxPageSize);
        var page = Math.Max(req.Page, 1);

        var q = db.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Images)
            .Where(p => p.Status == ProductStatus.Active);

        q = ApplyFilters(q, req);

        // InStockOnly: exclude products where base inventory exists and Available <= 0
        if (req.InStockOnly == true)
        {
            q = q.Where(p =>
                !db.InventoryItems.Any(inv =>
                    inv.ProductId == p.Id &&
                    inv.VariantId == null &&
                    (inv.OnHand - inv.Reserved) <= 0));
        }

        var total = await q.CountAsync(cancellationToken);
        q = ApplySort(q, req);

        var products = await q
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = products.Select(p => ProductMapper.MapToSummary(p)).ToList();
        return Result.Success(new PagedResponse<ProductSummaryResponse>(items, page, pageSize, total));
    }

    private static IQueryable<Product> ApplyFilters(IQueryable<Product> q, ProductQueryRequest req)
    {
        if (!string.IsNullOrWhiteSpace(req.Search))
        {
            var term = req.Search.Trim().ToLower();
            q = q.Where(p =>
                p.Name.ToLower().Contains(term) ||
                (p.Sku != null && p.Sku.ToLower().Contains(term)));
        }
        if (req.CategoryId.HasValue) q = q.Where(p => p.CategoryId == req.CategoryId);
        if (req.BrandId.HasValue) q = q.Where(p => p.BrandId == req.BrandId);
        if (req.MinPrice.HasValue) q = q.Where(p => p.Price >= req.MinPrice.Value);
        if (req.MaxPrice.HasValue) q = q.Where(p => p.Price <= req.MaxPrice.Value);
        if (req.IsFeatured.HasValue) q = q.Where(p => p.IsFeatured == req.IsFeatured.Value);
        return q;
    }

    private static IQueryable<Product> ApplySort(IQueryable<Product> q, ProductQueryRequest req)
    {
        var desc = string.Equals(req.SortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        return req.SortBy?.ToLowerInvariant() switch
        {
            "price"      => desc ? q.OrderByDescending(p => p.Price)      : q.OrderBy(p => p.Price),
            "created_at" => desc ? q.OrderByDescending(p => p.CreatedAtUtc) : q.OrderBy(p => p.CreatedAtUtc),
            "updated_at" => desc ? q.OrderByDescending(p => p.UpdatedAtUtc) : q.OrderBy(p => p.UpdatedAtUtc),
            _            => desc ? q.OrderByDescending(p => p.Name)       : q.OrderBy(p => p.Name)
        };
    }
}

internal sealed class GetAdminProductsHandler(IApplicationDbContext db)
    : IQueryHandler<GetAdminProductsQuery, PagedResponse<ProductSummaryResponse>>
{
    private const int MaxPageSize = 100;

    public async Task<Result<PagedResponse<ProductSummaryResponse>>> Handle(
        GetAdminProductsQuery query, CancellationToken cancellationToken)
    {
        var req = query.Request;
        if (!ProductSortFields.IsValid(req.SortBy))
            return Result.Failure<PagedResponse<ProductSummaryResponse>>(
                Error.Validation("INVALID_SORT_FIELD", $"Invalid sort field: {req.SortBy}."));

        var pageSize = Math.Clamp(req.PageSize, 1, MaxPageSize);
        var page = Math.Max(req.Page, 1);

        var q = db.Products.AsNoTracking()
            .Include(p => p.Category).Include(p => p.Brand).Include(p => p.Images)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(req.Search))
        {
            var term = req.Search.Trim().ToLower();
            q = q.Where(p =>
                p.Name.ToLower().Contains(term) ||
                (p.Sku != null && p.Sku.ToLower().Contains(term)));
        }
        if (req.CategoryId.HasValue) q = q.Where(p => p.CategoryId == req.CategoryId);
        if (req.BrandId.HasValue) q = q.Where(p => p.BrandId == req.BrandId);
        if (req.MinPrice.HasValue) q = q.Where(p => p.Price >= req.MinPrice.Value);
        if (req.MaxPrice.HasValue) q = q.Where(p => p.Price <= req.MaxPrice.Value);
        if (req.IsFeatured.HasValue) q = q.Where(p => p.IsFeatured == req.IsFeatured.Value);

        var total = await q.CountAsync(cancellationToken);

        var desc = string.Equals(req.SortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        q = req.SortBy?.ToLowerInvariant() switch
        {
            "price"      => desc ? q.OrderByDescending(p => p.Price)      : q.OrderBy(p => p.Price),
            "created_at" => desc ? q.OrderByDescending(p => p.CreatedAtUtc) : q.OrderBy(p => p.CreatedAtUtc),
            _            => desc ? q.OrderByDescending(p => p.Name)       : q.OrderBy(p => p.Name)
        };

        var products = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var items = products.Select(p => ProductMapper.MapToSummary(p)).ToList();
        return Result.Success(new PagedResponse<ProductSummaryResponse>(items, page, pageSize, total));
    }
}

internal sealed class GetProductBySlugHandler(IApplicationDbContext db)
    : IQueryHandler<GetProductBySlugQuery, ProductResponse>
{
    public async Task<Result<ProductResponse>> Handle(
        GetProductBySlugQuery query, CancellationToken cancellationToken)
    {
        var p = await db.Products.AsNoTracking()
            .Include(x => x.Category).Include(x => x.Brand)
            .Include(x => x.Images).Include(x => x.Attributes).ThenInclude(a => a.Values)
            .Include(x => x.Variants)
            .FirstOrDefaultAsync(
                x => x.Slug == query.Slug && x.Status == ProductStatus.Active,
                cancellationToken);

        return p is null
            ? Result.Failure<ProductResponse>(Error.NotFound("PRODUCT_NOT_FOUND", "Product not found."))
            : Result.Success(ProductMapper.MapToResponse(p));
    }
}

internal sealed class GetProductByIdHandler(IApplicationDbContext db)
    : IQueryHandler<GetProductByIdQuery, ProductResponse>
{
    public async Task<Result<ProductResponse>> Handle(
        GetProductByIdQuery query, CancellationToken cancellationToken)
    {
        var p = await db.Products.AsNoTracking()
            .Include(x => x.Category).Include(x => x.Brand)
            .Include(x => x.Images).Include(x => x.Attributes).ThenInclude(a => a.Values)
            .Include(x => x.Variants)
            .FirstOrDefaultAsync(x => x.Id == query.Id, cancellationToken);

        return p is null
            ? Result.Failure<ProductResponse>(Error.NotFound("PRODUCT_NOT_FOUND", "Product not found."))
            : Result.Success(ProductMapper.MapToResponse(p));
    }
}
