using KromicCommerce.Contracts.Common;
using KromicCommerce.Contracts.Promotions;
using KromicCommerce.Domain.Promotions;

namespace KromicCommerce.Application.Features.Admin.Promotions;

// -----------------------------------------------------------------------
// Create
// -----------------------------------------------------------------------

internal sealed class CreatePromotionHandler(IApplicationDbContext db)
    : ICommandHandler<CreatePromotionCommand, PromotionDetailResponse>
{
    public async Task<Result<PromotionDetailResponse>> Handle(
        CreatePromotionCommand cmd, CancellationToken ct)
    {
        var normalised = Promotion.NormaliseCouponCode(cmd.CouponCode);

        if (await db.Promotions.AnyAsync(p => p.CouponCode == normalised, ct))
            return Result.Failure<PromotionDetailResponse>(
                Error.Conflict("COUPON_CODE_EXISTS", $"Coupon code '{normalised}' is already in use."));

        Promotion promotion;
        try
        {
            promotion = Promotion.Create(
                cmd.Name, cmd.Description, cmd.CouponCode,
                cmd.DiscountType, cmd.DiscountValue, cmd.MaxDiscountAmount,
                cmd.MinimumOrderAmount, cmd.UsageLimit, cmd.PerCustomerUsageLimit,
                cmd.StartsAt, cmd.ExpiresAt, cmd.Applicability, cmd.IsFirstOrderOnly);
        }
        catch (ArgumentException ex)
        {
            return Result.Failure<PromotionDetailResponse>(
                Error.Validation("PROMOTION_INVALID", ex.Message));
        }

        if (cmd.Applicability == PromotionApplicabilityType.SpecificProducts && cmd.TargetProductIds?.Count > 0)
            promotion.SetTargetProducts(cmd.TargetProductIds);
        if (cmd.Applicability == PromotionApplicabilityType.SpecificCategories && cmd.TargetCategoryIds?.Count > 0)
            promotion.SetTargetCategories(cmd.TargetCategoryIds);

        db.Promotions.Add(promotion);
        await db.SaveChangesAsync(ct);

        return Result.Success(PromotionHandlers.MapDetail(promotion));
    }
}

// -----------------------------------------------------------------------
// Update
// -----------------------------------------------------------------------

internal sealed class UpdatePromotionHandler(IApplicationDbContext db)
    : ICommandHandler<UpdatePromotionCommand, PromotionDetailResponse>
{
    public async Task<Result<PromotionDetailResponse>> Handle(
        UpdatePromotionCommand cmd, CancellationToken ct)
    {
        var promotion = await db.Promotions
            .Include(p => p.Products)
            .Include(p => p.Categories)
            .FirstOrDefaultAsync(p => p.Id == cmd.Id, ct);

        if (promotion is null)
            return Result.Failure<PromotionDetailResponse>(
                Error.NotFound("PROMOTION_NOT_FOUND", "Promotion not found."));

        try
        {
            promotion.Update(
                cmd.Name, cmd.Description, cmd.DiscountType, cmd.DiscountValue,
                cmd.MaxDiscountAmount, cmd.MinimumOrderAmount, cmd.UsageLimit,
                cmd.PerCustomerUsageLimit, cmd.StartsAt, cmd.ExpiresAt,
                cmd.Applicability, cmd.IsFirstOrderOnly);
        }
        catch (ArgumentException ex)
        {
            return Result.Failure<PromotionDetailResponse>(
                Error.Validation("PROMOTION_INVALID", ex.Message));
        }

        if (cmd.Applicability == PromotionApplicabilityType.SpecificProducts)
            promotion.SetTargetProducts(cmd.TargetProductIds ?? []);
        else if (cmd.Applicability == PromotionApplicabilityType.SpecificCategories)
            promotion.SetTargetCategories(cmd.TargetCategoryIds ?? []);

        await db.SaveChangesAsync(ct);
        return Result.Success(PromotionHandlers.MapDetail(promotion));
    }
}

// -----------------------------------------------------------------------
// Activate / Deactivate / Delete
// -----------------------------------------------------------------------

internal sealed class ActivatePromotionHandler(IApplicationDbContext db)
    : ICommandHandler<ActivatePromotionCommand>
{
    public async Task<Result> Handle(ActivatePromotionCommand cmd, CancellationToken ct)
    {
        var promotion = await db.Promotions.FirstOrDefaultAsync(p => p.Id == cmd.Id, ct);
        if (promotion is null)
            return Result.Failure(Error.NotFound("PROMOTION_NOT_FOUND", "Promotion not found."));

        promotion.Activate();
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal sealed class DeactivatePromotionHandler(IApplicationDbContext db)
    : ICommandHandler<DeactivatePromotionCommand>
{
    public async Task<Result> Handle(DeactivatePromotionCommand cmd, CancellationToken ct)
    {
        var promotion = await db.Promotions.FirstOrDefaultAsync(p => p.Id == cmd.Id, ct);
        if (promotion is null)
            return Result.Failure(Error.NotFound("PROMOTION_NOT_FOUND", "Promotion not found."));

        promotion.Deactivate();
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal sealed class DeletePromotionHandler(IApplicationDbContext db)
    : ICommandHandler<DeletePromotionCommand>
{
    public async Task<Result> Handle(DeletePromotionCommand cmd, CancellationToken ct)
    {
        var promotion = await db.Promotions.FirstOrDefaultAsync(p => p.Id == cmd.Id, ct);
        if (promotion is null)
            return Result.Failure(Error.NotFound("PROMOTION_NOT_FOUND", "Promotion not found."));

        if (promotion.IsActive)
            return Result.Failure(
                Error.Conflict("PROMOTION_ACTIVE", "Deactivate the promotion before deleting it."));

        var hasUsage = await db.PromotionUsages.AnyAsync(u => u.PromotionId == cmd.Id, ct);
        if (hasUsage)
            return Result.Failure(
                Error.Conflict("PROMOTION_HAS_USAGE",
                    "Cannot delete a promotion that has been used. Deactivate it instead."));

        db.Promotions.Remove(promotion);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

// -----------------------------------------------------------------------
// List + Get
// -----------------------------------------------------------------------

internal sealed class GetPromotionsHandler(IApplicationDbContext db)
    : IQueryHandler<GetPromotionsQuery, PagedResponse<PromotionSummaryResponse>>
{
    public async Task<Result<PagedResponse<PromotionSummaryResponse>>> Handle(
        GetPromotionsQuery query, CancellationToken ct)
    {
        var q = db.Promotions.AsQueryable();

        if (query.IsActive.HasValue)
            q = q.Where(p => p.IsActive == query.IsActive.Value);

        if (!string.IsNullOrWhiteSpace(query.CouponCode))
        {
            var norm = Promotion.NormaliseCouponCode(query.CouponCode);
            q = q.Where(p => p.CouponCode.Contains(norm));
        }

        if (query.StartFrom.HasValue)
            q = q.Where(p => p.StartsAt >= query.StartFrom.Value);
        if (query.StartTo.HasValue)
            q = q.Where(p => p.StartsAt <= query.StartTo.Value);

        q = (query.SortBy, query.Descending) switch
        {
            ("Name",         true)  => q.OrderByDescending(p => p.Name),
            ("Name",         false) => q.OrderBy(p => p.Name),
            ("CouponCode",   true)  => q.OrderByDescending(p => p.CouponCode),
            ("CouponCode",   false) => q.OrderBy(p => p.CouponCode),
            ("StartsAt",     true)  => q.OrderByDescending(p => p.StartsAt),
            ("StartsAt",     false) => q.OrderBy(p => p.StartsAt),
            ("ExpiresAt",    true)  => q.OrderByDescending(p => p.ExpiresAt),
            ("ExpiresAt",    false) => q.OrderBy(p => p.ExpiresAt),
            ("UsageCount",   true)  => q.OrderByDescending(p => p.UsageCount),
            ("UsageCount",   false) => q.OrderBy(p => p.UsageCount),
            ("IsActive",     true)  => q.OrderByDescending(p => p.IsActive),
            ("IsActive",     false) => q.OrderBy(p => p.IsActive),
            _                       => q.OrderByDescending(p => p.CreatedAtUtc)
        };

        var total = await q.CountAsync(ct);
        var skip = (query.Page - 1) * query.PageSize;

        var items = await q.Skip(skip).Take(query.PageSize).Select(p => new PromotionSummaryResponse(
            p.Id, p.Name, p.CouponCode, p.DiscountType.ToString(),
            p.DiscountValue, p.MaxDiscountAmount, p.IsActive, p.UsageCount,
            p.UsageLimit, p.StartsAt, p.ExpiresAt, p.Applicability.ToString(),
            p.CreatedAtUtc)).ToListAsync(ct);

        return Result.Success(new PagedResponse<PromotionSummaryResponse>(
            items, total, query.Page, query.PageSize));
    }
}

internal sealed class GetPromotionByIdHandler(IApplicationDbContext db)
    : IQueryHandler<GetPromotionByIdQuery, PromotionDetailResponse>
{
    public async Task<Result<PromotionDetailResponse>> Handle(
        GetPromotionByIdQuery query, CancellationToken ct)
    {
        var promotion = await db.Promotions
            .Include(p => p.Products)
            .Include(p => p.Categories)
            .FirstOrDefaultAsync(p => p.Id == query.Id, ct);

        if (promotion is null)
            return Result.Failure<PromotionDetailResponse>(
                Error.NotFound("PROMOTION_NOT_FOUND", "Promotion not found."));

        return Result.Success(PromotionHandlers.MapDetail(promotion));
    }
}

// -----------------------------------------------------------------------
// Mapping helper
// -----------------------------------------------------------------------

file static class PromotionHandlers
{
    public static PromotionDetailResponse MapDetail(Promotion p) => new(
        p.Id, p.Name, p.Description, p.CouponCode,
        p.DiscountType.ToString(), p.DiscountValue, p.MaxDiscountAmount,
        p.MinimumOrderAmount, p.UsageLimit, p.PerCustomerUsageLimit,
        p.StartsAt, p.ExpiresAt, p.Applicability.ToString(),
        p.IsFirstOrderOnly, p.IsActive, p.UsageCount,
        p.Products.Select(pp => pp.ProductId).ToList(),
        p.Categories.Select(pc => pc.CategoryId).ToList(),
        p.CreatedAtUtc, p.UpdatedAtUtc);
}
