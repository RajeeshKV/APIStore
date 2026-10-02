namespace KromicCommerce.Application.Features.Catalog.Reviews;

internal sealed class GetMyReviewsHandler(
    IApplicationDbContext db)
    : IQueryHandler<GetMyReviewsQuery, PagedResponse<MyReviewResponse>>
{
    public async Task<Result<PagedResponse<MyReviewResponse>>> Handle(
        GetMyReviewsQuery query, CancellationToken ct)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 50);

        var baseQuery = db.ProductReviews
            .AsNoTracking()
            .Where(r => r.CustomerId == query.CustomerId);

        var total = await baseQuery.CountAsync(ct);

        // No filter on Status: a customer must be able to see their Pending and Rejected reviews
        // in order to understand why they are not visible.
        var rows = await baseQuery
            .OrderByDescending(r => r.CreatedAtUtc).ThenBy(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(r => r.Images)
            .Select(r => new { Review = r })
            .ToListAsync(ct);

        var productIds = rows.Select(r => r.Review.ProductId).Distinct().ToList();
        var names = (await db.Products
                .AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Name })
                .ToListAsync(ct))
            .ToDictionary(x => x.Id, x => x.Name);

        var items = rows.Select(r =>
        {
            names.TryGetValue(r.Review.ProductId, out var name);
            return ReviewMapper.MapMine(r.Review, name ?? string.Empty);
        }).ToList();

        return Result.Success(new PagedResponse<MyReviewResponse>(items, page, pageSize, total));
    }
}

internal sealed class GetAdminReviewsHandler(
    IApplicationDbContext db)
    : IQueryHandler<GetAdminReviewsQuery, PagedResponse<AdminReviewResponse>>
{
    public async Task<Result<PagedResponse<AdminReviewResponse>>> Handle(
        GetAdminReviewsQuery query, CancellationToken ct)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var baseQuery = db.ProductReviews.AsNoTracking();

        if (query.Status.HasValue)
            baseQuery = baseQuery.Where(r => r.Status == query.Status.Value);

        if (query.ProductId.HasValue)
            baseQuery = baseQuery.Where(r => r.ProductId == query.ProductId.Value);

        if (query.Rating.HasValue)
            baseQuery = baseQuery.Where(r => r.Rating == query.Rating.Value);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            baseQuery = baseQuery.Where(r =>
                (r.Title != null && r.Title.ToLower().Contains(term)) ||
                r.Body.ToLower().Contains(term));
        }

        var total = await baseQuery.CountAsync(ct);

        // CreatedAtUtc then Id: a moderator paging the queue must not see rows skip or repeat
        // because two reviews share a timestamp.
        var rows = await baseQuery
            .OrderByDescending(r => r.CreatedAtUtc).ThenBy(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(r => r.Images)
            .Select(r => new { Review = r })
            .ToListAsync(ct);

        var items = await HydrateAsync(db, rows.Select(r => r.Review).ToList(), ct);

        return Result.Success(new PagedResponse<AdminReviewResponse>(items, page, pageSize, total));
    }

    internal static async Task<List<AdminReviewResponse>> HydrateAsync(
        IApplicationDbContext db, List<ProductReview> reviews, CancellationToken ct)
    {
        var productIds = reviews.Select(r => r.ProductId).Distinct().ToList();
        var customerIds = reviews.Select(r => r.CustomerId).Distinct().ToList();

        var products = (await db.Products
                .AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Name })
                .ToListAsync(ct))
            .ToDictionary(x => x.Id, x => x.Name);

        var users = (await db.Users
                .AsNoTracking()
                .Where(u => customerIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Email })
                .ToListAsync(ct))
            .ToDictionary(x => x.Id, x => x.Email);

        return reviews
            .Select(r =>
            {
                products.TryGetValue(r.ProductId, out var name);
                users.TryGetValue(r.CustomerId, out var email);
                return ReviewMapper.MapAdmin(r, name ?? string.Empty, email ?? string.Empty);
            })
            .ToList();
    }
}

internal sealed class GetAdminReviewHandler(
    IApplicationDbContext db)
    : IQueryHandler<GetAdminReviewQuery, AdminReviewResponse>
{
    public async Task<Result<AdminReviewResponse>> Handle(
        GetAdminReviewQuery query, CancellationToken ct)
    {
        var review = await db.ProductReviews
            .AsNoTracking()
            .Include(r => r.Images)
            .FirstOrDefaultAsync(r => r.Id == query.ReviewId, ct);

        if (review is null)
            return Result.Failure<AdminReviewResponse>(
                Error.NotFound("PRODUCT_REVIEW_NOT_FOUND", "Review not found."));

        var items = await GetAdminReviewsHandler.HydrateAsync(db, [review], ct);
        return Result.Success(items[0]);
    }
}