using KromicCommerce.Application.Caching;

namespace KromicCommerce.Application.Features.Catalog.Reviews;

internal sealed class SubmitReviewHandler(
    IApplicationDbContext db,
    ProductReviewRatingRecalculator ratings,
    ICatalogCacheService cache,
    ILogger<SubmitReviewHandler> logger) : ICommandHandler<SubmitReviewCommand, MyReviewResponse>
{
    public async Task<Result<MyReviewResponse>> Handle(SubmitReviewCommand cmd, CancellationToken ct)
    {
        var target = await ReviewTargetResolver.ResolveAsync(db, cmd.ProductId, cmd.ProductVariantId, ct);
        if (target.IsFailure) return Result.Failure<MyReviewResponse>(target.Error);

        var images = ReviewImageFactory.Build(cmd.Images);
        if (images.IsFailure) return Result.Failure<MyReviewResponse>(images.Error);

        // Server-derived, never client-supplied.
        var verified = await ReviewTargetResolver.IsVerifiedPurchaseAsync(
            db, cmd.CustomerId, cmd.ProductId, ct);

        // Submissions are published immediately.
        //
        // Reviews were previously held as Pending and only became visible after an admin published
        // them, which meant a customer who had just written a review saw no trace of it, and the
        // product page kept advertising a rating that no longer matched anything. Pre-publication
        // screening is a policy decision, not a technical requirement, and the mechanisms for
        // acting after the fact are stronger: IsVerifiedPurchase marks genuine buyers, and an admin
        // can return a review to Pending, reject it with a reason, or delete it outright.
        //
        // PublishedAtUtc is stamped by the context method, so the review enters the public list —
        // and the rating aggregate — in the same save as its body.
        var status = ReviewStatus.Published;

        // The unique index decides the winner. The handler does not pre-check for an existing
        // review: a read-then-write here loses to a concurrent submit, and the ON CONFLICT
        // DO NOTHING inside the context method is what actually prevents the duplicate row.
        var created = await db.TryAddProductReviewAsync(
            cmd.CustomerId, cmd.ProductId, cmd.ProductVariantId,
            cmd.Rating, cmd.Title?.Trim(), cmd.Body, verified, status, ct);

        if (!created)
            return Result.Failure<MyReviewResponse>(Error.Conflict(
                "PRODUCT_REVIEW_ALREADY_EXISTS",
                "You have already reviewed this product."));

        // Load it back so images, timestamps, and the persisted status all come from the
        // database rather than from the values this request hoped to store.
        var review = await db.ProductReviews
            .Include(r => r.Images)
            .FirstAsync(r =>
                r.CustomerId == cmd.CustomerId &&
                r.ProductId == cmd.ProductId &&
                r.ProductVariantId == cmd.ProductVariantId, ct);

        foreach (var asset in images.Value)
            review.AddImage(asset, review.Images.Count);

        // The review is inserted as Published, so it is already part of the published set the
        // recalculator reads — it counts this review, and the aggregate moves in the same save.
        await ratings.RecalculateAsync(cmd.ProductId, ct);
        await db.SaveChangesAsync(ct);

        // RatingAverage/RatingCount live on Product, so the whole product graph is invalidated,
        // not just the review list: the product page and the featured list both embed them.
        cache.InvalidateProductGraph(cmd.ProductId, target.Value.Product.Slug);
        cache.InvalidateProductReviews(cmd.ProductId);

        logger.LogInformation(
            "Review submitted by {CustomerId} for product {ProductId}: status={Status}, verified={Verified}",
            cmd.CustomerId, cmd.ProductId, status, verified);

        return Result.Success(ReviewMapper.MapMine(review, target.Value.Product.Name));
    }
}

internal sealed class EditReviewHandler(
    IApplicationDbContext db,
    ProductReviewRatingRecalculator ratings,
    ICatalogCacheService cache,
    ILogger<EditReviewHandler> logger) : ICommandHandler<EditReviewCommand, MyReviewResponse>
{
    public async Task<Result<MyReviewResponse>> Handle(EditReviewCommand cmd, CancellationToken ct)
    {
        // Scoped to the owner. A non-owner gets NotFound rather than Forbidden so the endpoint
        // does not confirm that a given review id exists.
        var review = await db.ProductReviews
            .Include(r => r.Images)
            .FirstOrDefaultAsync(r => r.Id == cmd.ReviewId && r.CustomerId == cmd.CustomerId, ct);

        if (review is null)
            return Result.Failure<MyReviewResponse>(
                Error.NotFound("PRODUCT_REVIEW_NOT_FOUND", "Review not found."));

        var images = ReviewImageFactory.Build(cmd.Images);
        if (images.IsFailure) return Result.Failure<MyReviewResponse>(images.Error);

        // Content only. Status, PublishedAtUtc, IsVerifiedPurchase and ownership are untouched:
        // editing a published review does not silently re-enter it into moderation.
        review.Edit(cmd.Rating, cmd.Title, cmd.Body);

        if (cmd.Images is not null)
        {
            // Replace the set: remove-then-add keeps the max-images guard honest and avoids
            // accumulating orphans across repeated edits.
            foreach (var existing in review.Images.ToList())
                review.RemoveImage(existing.Id);

            foreach (var asset in images.Value)
                review.AddImage(asset, review.Images.Count);
        }

        var (productName, productSlug) = await ReviewMapper.LoadProductAsync(db, review.ProductId, ct);

        // Editing a Published review changes the rating aggregate (a new star value may land
        // in a different bucket), so it must be recalculated, not just status changes.
        await ratings.RecalculateAsync(review.ProductId, ct);
        await db.SaveChangesAsync(ct);

        cache.InvalidateProductGraph(review.ProductId, productSlug);
        cache.InvalidateProductReviews(review.ProductId);

        logger.LogInformation("Review {ReviewId} edited by owner", cmd.ReviewId);

        return Result.Success(ReviewMapper.MapMine(review, productName));
    }
}

internal sealed class DeleteReviewHandler(
    IApplicationDbContext db,
    ProductReviewRatingRecalculator ratings,
    ICatalogCacheService cache,
    ILogger<DeleteReviewHandler> logger) : ICommandHandler<DeleteReviewCommand>
{
    public async Task<Result> Handle(DeleteReviewCommand cmd, CancellationToken ct)
    {
        var review = await db.ProductReviews
            .FirstOrDefaultAsync(r => r.Id == cmd.ReviewId && r.CustomerId == cmd.CustomerId, ct);

        if (review is null)
            return Result.Failure(
                Error.NotFound("PRODUCT_REVIEW_NOT_FOUND", "Review not found."));

        var productId = review.ProductId;
        review.RaiseDeleted();
        db.ProductReviews.Remove(review);

        var (_, productSlug) = await ReviewMapper.LoadProductAsync(db, productId, ct);

        // Hard delete, matching the rest of the schema. Review images and helpful votes cascade
        // at the database level. The Cloudinary assets are deliberately left in place — deleting
        // them here would destroy content that a re-submitted review may reuse.
        await ratings.RecalculateAsync(productId, ct);
        await db.SaveChangesAsync(ct);

        cache.InvalidateProductGraph(productId, productSlug);
        cache.InvalidateProductReviews(productId);

        logger.LogInformation("Review {ReviewId} deleted by owner", cmd.ReviewId);

        return Result.Success();
    }
}

internal sealed class ToggleReviewHelpfulHandler(
    IApplicationDbContext db,
    ProductReviewRatingRecalculator ratings,
    ICatalogCacheService cache) : ICommandHandler<ToggleReviewHelpfulCommand, ReviewHelpfulResponse>
{
    public async Task<Result<ReviewHelpfulResponse>> Handle(
        ToggleReviewHelpfulCommand cmd, CancellationToken ct)
    {
        var review = await db.ProductReviews
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == cmd.ReviewId, ct);

        if (review is null || review.Status != ReviewStatus.Published)
            return Result.Failure<ReviewHelpfulResponse>(
                Error.NotFound("PRODUCT_REVIEW_NOT_FOUND", "Review not found."));

        // A review endorsing itself is not a signal, and the unique (ReviewId, CustomerId)
        // index would otherwise let one account inflate a competitor's review.
        if (review.CustomerId == cmd.CustomerId)
            return Result.Failure<ReviewHelpfulResponse>(
                Error.Validation("PRODUCT_REVIEW_SELF_VOTE", "You cannot vote on your own review."));

        var existingVote = await db.ReviewHelpfulVotes
            .FirstOrDefaultAsync(v => v.ReviewId == cmd.ReviewId && v.CustomerId == cmd.CustomerId, ct);

        var isHelpful = existingVote is null;
        if (existingVote is null)
            db.ReviewHelpfulVotes.Add(ReviewHelpfulVote.Create(cmd.ReviewId, cmd.CustomerId));
        else
            db.ReviewHelpfulVotes.Remove(existingVote);

        // Recalculated from the vote rows so the count can fall back to zero when the last vote
        // is withdrawn — an increment-only counter can never do that.
        await ratings.RecalculateHelpfulCountsAsync([cmd.ReviewId], ct);
        await db.SaveChangesAsync(ct);

        var count = await db.ReviewHelpfulVotes
            .AsNoTracking()
            .CountAsync(v => v.ReviewId == cmd.ReviewId, ct);

        cache.InvalidateProductReviews(review.ProductId);

        return Result.Success(new ReviewHelpfulResponse(isHelpful, count));
    }
}

internal sealed class ModerateReviewHandler(
    IApplicationDbContext db,
    ProductReviewRatingRecalculator ratings,
    ICatalogCacheService cache,
    ILogger<ModerateReviewHandler> logger) : ICommandHandler<ModerateReviewCommand, AdminReviewResponse>
{
    public async Task<Result<AdminReviewResponse>> Handle(
        ModerateReviewCommand cmd, CancellationToken ct)
    {
        var review = await db.ProductReviews
            .Include(r => r.Images)
            .FirstOrDefaultAsync(r => r.Id == cmd.ReviewId, ct);

        if (review is null)
            return Result.Failure<AdminReviewResponse>(
                Error.NotFound("PRODUCT_REVIEW_NOT_FOUND", "Review not found."));

        if (cmd.Status == ReviewStatus.Rejected && string.IsNullOrWhiteSpace(cmd.Reason))
            return Result.Failure<AdminReviewResponse>(Error.Validation(
                "REVIEW_REASON_REQUIRED", "A reason is required when rejecting a review."));

        review.SetStatus(cmd.Status, cmd.Reason);

        // Publication is what puts a review in the aggregate, so every moderation decision
        // recalculates — including rejecting something that was already Pending.
        await ratings.RecalculateAsync(review.ProductId, ct);
        await db.SaveChangesAsync(ct);

        var (productName, productSlug) = await ReviewMapper.LoadProductAsync(db, review.ProductId, ct);
        var customerEmail = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == review.CustomerId)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        cache.InvalidateProductGraph(review.ProductId, productSlug);
        cache.InvalidateProductReviews(review.ProductId);

        logger.LogInformation(
            "Review {ReviewId} moderated to {Status} by admin", cmd.ReviewId, cmd.Status);

        return Result.Success(ReviewMapper.MapAdmin(review, productName, customerEmail));
    }
}

internal sealed class AdminDeleteReviewHandler(
    IApplicationDbContext db,
    ProductReviewRatingRecalculator ratings,
    ICatalogCacheService cache,
    ILogger<AdminDeleteReviewHandler> logger) : ICommandHandler<AdminDeleteReviewCommand>
{
    public async Task<Result> Handle(AdminDeleteReviewCommand cmd, CancellationToken ct)
    {
        var review = await db.ProductReviews
            .FirstOrDefaultAsync(r => r.Id == cmd.ReviewId, ct);

        if (review is null)
            return Result.Failure(
                Error.NotFound("PRODUCT_REVIEW_NOT_FOUND", "Review not found."));

        var productId = review.ProductId;
        review.RaiseDeleted();
        db.ProductReviews.Remove(review);

        var (_, productSlug) = await ReviewMapper.LoadProductAsync(db, productId, ct);

        await ratings.RecalculateAsync(productId, ct);
        await db.SaveChangesAsync(ct);

        cache.InvalidateProductGraph(productId, productSlug);
        cache.InvalidateProductReviews(productId);

        logger.LogInformation("Review {ReviewId} deleted by admin", cmd.ReviewId);

        return Result.Success();
    }
}