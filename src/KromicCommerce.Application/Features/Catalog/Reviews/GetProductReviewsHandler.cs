using KromicCommerce.Application.Caching;
using Microsoft.Extensions.Caching.Memory;

namespace KromicCommerce.Application.Features.Catalog.Reviews;

internal sealed class GetProductReviewsHandler(
    IApplicationDbContext db,
    IMemoryCache cache,
    IOptions<CatalogCacheOptions> cacheOpts)
    : IQueryHandler<GetProductReviewsQuery, ReviewListResponse>
{
    private const int MaxPageSize = 50;

    public async Task<Result<ReviewListResponse>> Handle(
        GetProductReviewsQuery query, CancellationToken ct)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var sort = query.Sort?.ToLowerInvariant() ?? "recent";

        // Every varying input is part of the key. Omitting one would serve a page built for a
        // different product, page, sort, or rating filter. The epoch is embedded too, so
        // InvalidateProductReviews orphans every cached page for this product at once.
        var key = CatalogCacheKeys.StorefrontProductReviewsWithEpoch(
            query.ProductId,
            CatalogCacheKeys.CurrentProductReviewsEpoch(cache, query.ProductId),
            sort,
            query.Rating,
            page,
            pageSize);

        if (cache.TryGetValue(key, out ReviewListResponse? cached) && cached is not null)
            return Result.Success(cached);

        var baseQuery = db.ProductReviews
            .AsNoTracking()
            // Only Published is ever public. A Pending review belongs to its author and the
            // moderation queue, never to a storefront response.
            .Where(r => r.ProductId == query.ProductId && r.Status == ReviewStatus.Published);

        if (query.Rating.HasValue)
            baseQuery = baseQuery.Where(r => r.Rating == query.Rating.Value);

        var total = await baseQuery.CountAsync(ct);

        // Every sort ends in PublishedAtUtc then Id. Without that tail two reviews sharing a
        // sort key could swap between page requests and make one appear to vanish.
        var ordered = sort switch
        {
            "helpful" => baseQuery
                .OrderByDescending(r => r.HelpfulCount)
                .ThenByDescending(r => r.PublishedAtUtc).ThenBy(r => r.Id),
            "rating" => baseQuery
                .OrderByDescending(r => r.Rating)
                .ThenByDescending(r => r.PublishedAtUtc).ThenBy(r => r.Id),
            _ => baseQuery
                .OrderByDescending(r => r.PublishedAtUtc).ThenBy(r => r.Id)
        };

        var rows = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(r => r.Images)
            .Select(r => new { Review = r, AuthorId = r.CustomerId })
            .ToListAsync(ct);

        var authorIds = rows.Select(r => r.AuthorId).Distinct().ToList();
        var authors = await db.Users
            .AsNoTracking()
            .Where(u => authorIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email })
            .ToDictionaryAsync(u => u.Id, ct);

        var items = rows.Select(r =>
        {
            var name = authors.TryGetValue(r.AuthorId, out var a)
                ? ReviewMapper.AuthorName(a.FirstName, a.LastName, a.Email)
                : "Customer";
            return ReviewMapper.MapPublic(r.Review, name);
        }).ToList();

        // The aggregate is computed from the whole published set, not from the page, so paging
        // through reviews never changes the summary block at the top of the page.
        var allRatings = await db.ProductReviews
            .AsNoTracking()
            .Where(r => r.ProductId == query.ProductId && r.Status == ReviewStatus.Published)
            .Select(r => r.Rating)
            .ToListAsync(ct);

        var aggregate = ReviewRatingAggregate.FromRatings(allRatings);
        var breakdown = new Dictionary<int, int>();
        foreach (var r in allRatings)
            breakdown[r] = breakdown.GetValueOrDefault(r) + 1;

        var response = new ReviewListResponse(
            new PagedResponse<ProductReviewSummaryResponse>(items, page, pageSize, total),
            aggregate.Average,
            aggregate.Count,
            new ReviewRatingBreakdown(breakdown));

        cache.Set(key, response, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(cacheOpts.Value.DefaultExpiryMinutes),
            Size = 1
        });

        return Result.Success(response);
    }
}