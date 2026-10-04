using KromicCommerce.Application.Abstractions.Catalog;
using KromicCommerce.Application.Caching;
using KromicCommerce.Application.Features.Catalog.Reviews;
using KromicCommerce.Application.Options;
using KromicCommerce.Contracts.Catalog;
using KromicCommerce.Domain.Catalog;
using KromicCommerce.Domain.Common;
using KromicCommerce.Domain.Identity;
using KromicCommerce.Infrastructure.Persistence;
using KromicCommerce.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace KromicCommerce.IntegrationTests.Catalog;

/// <summary>
/// Reviews are published on submission. These tests pin that policy and the levers an admin still
/// has afterwards, because both halves matter: the review must be visible the moment it is
/// written, and taking it down must be just as immediate.
/// </summary>
/// <remarks>
/// Reviews were previously created Pending and only appeared after an admin published them. A
/// customer who had just written one saw nothing anywhere, and the product page kept advertising a
/// rating that no longer matched anything. The admin surface is unchanged — status is still the
/// only lever — so the moderation paths below are as much a part of the policy as the submit path.
/// </remarks>
[Collection("Database")]
public sealed class ReviewAutoPublishTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    // -----------------------------------------------------------------------
    // Auto-publish on submit
    // -----------------------------------------------------------------------

    [SkippableFact]
    public async Task A_submitted_review_is_published_and_visible_immediately()
    {
        var customer = await SeedCustomerAsync();
        var product = await SeedProductAsync();

        var response = await SubmitAsync(customer, product, 5, "Super", "Great service");

        response.Status.Should().Be("Published");
        response.PublishedAtUtc.Should().NotBeNull();

        // Not just flagged published — actually readable by the storefront with no admin step.
        var listing = await Storefront().Handle(
            new GetProductReviewsQuery(product, 1, 20), CancellationToken.None);

        listing.Value.Page.Items.Should().ContainSingle()
            .Which.Id.Should().Be(response.Id);
    }

    [SkippableFact]
    public async Task A_submitted_review_moves_the_rating_aggregate_in_the_same_request()
    {
        // The aggregate is recalculated inside the submit handler's own save. If it were deferred,
        // the product page would briefly advertise a rating that excludes the review the customer
        // is looking at.
        var customer = await SeedCustomerAsync();
        var product = await SeedProductAsync();

        await SubmitAsync(customer, product, 4, null, "Solid");

        (await AggregateAsync(product)).Should().Be((4.00m, 1));
    }

    [SkippableFact]
    public async Task A_second_submission_updates_the_aggregate_to_both_reviews()
    {
        // The exact production report: two published reviews, product still advertising one.
        var product = await SeedProductAsync();

        await SubmitAsync(await SeedCustomerAsync(), product, 5, null, "Excellent");
        await SubmitAsync(await SeedCustomerAsync(), product, 4, null, "Good");

        (await AggregateAsync(product)).Should().Be((4.50m, 2));

        // And the stored summary agrees with what the reviews endpoint computes live.
        var listing = await Storefront().Handle(
            new GetProductReviewsQuery(product, 1, 20), CancellationToken.None);

        listing.Value.RatingCount.Should().Be(2);
        listing.Value.RatingAverage.Should().Be(4.50m);
    }

    [SkippableFact]
    public async Task A_submitted_review_appears_in_the_authors_own_list()
    {
        var customer = await SeedCustomerAsync();
        var product = await SeedProductAsync();

        await SubmitAsync(customer, product, 5, null, "Mine");

        var mine = await Mine().Handle(new GetMyReviewsQuery(customer, 1, 20), CancellationToken.None);

        mine.Value.Items.Should().ContainSingle();
    }

    // -----------------------------------------------------------------------
    // The admin levers, which are what replaced the publish gate
    // -----------------------------------------------------------------------

    [SkippableFact]
    public async Task An_admin_can_return_a_review_to_pending_and_it_leaves_the_storefront()
    {
        var customer = await SeedCustomerAsync();
        var product = await SeedProductAsync();
        var review = await SubmitAsync(customer, product, 5, null, "Buy me");

        (await ModerateAsync(review.Id, ReviewStatus.Pending)).IsSuccess.Should().BeTrue();

        (await Storefront().Handle(new GetProductReviewsQuery(product, 1, 20), CancellationToken.None))
            .Value.Page.Items.Should().BeEmpty();
        (await AggregateAsync(product)).Should().Be((0m, 0));
    }

    [SkippableFact]
    public async Task An_admin_can_reject_a_review_and_it_leaves_the_storefront()
    {
        var customer = await SeedCustomerAsync();
        var product = await SeedProductAsync();
        var review = await SubmitAsync(customer, product, 1, null, "Spam");

        var rejected = await ModerateAsync(review.Id, ReviewStatus.Rejected, "Spam");
        rejected.IsSuccess.Should().BeTrue();
        rejected.Value.Status.Should().Be("Rejected");
        rejected.Value.ModerationReason.Should().Be("Spam");

        (await Storefront().Handle(new GetProductReviewsQuery(product, 1, 20), CancellationToken.None))
            .Value.Page.Items.Should().BeEmpty();
        (await AggregateAsync(product)).Should().Be((0m, 0));
    }

    [SkippableFact]
    public async Task An_admin_can_delete_a_review_and_it_leaves_the_storefront()
    {
        var customer = await SeedCustomerAsync();
        var product = await SeedProductAsync();
        var review = await SubmitAsync(customer, product, 5, null, "Remove me");

        var deleted = await DeleteAsync(review.Id);
        deleted.IsSuccess.Should().BeTrue();

        (await Storefront().Handle(new GetProductReviewsQuery(product, 1, 20), CancellationToken.None))
            .Value.Page.Items.Should().BeEmpty();
        (await AggregateAsync(product)).Should().Be((0m, 0));
    }

    /// <summary>
    /// The author's own list deliberately has no status filter, so a review an admin has pulled is
    /// still visible to them — they can see it exists and edit or delete it rather than wondering
    /// whether it saved.
    /// </summary>
    [SkippableFact]
    public async Task An_author_still_sees_a_review_an_admin_made_pending()
    {
        var customer = await SeedCustomerAsync();
        var product = await SeedProductAsync();
        var review = await SubmitAsync(customer, product, 5, null, "Still here");

        await ModerateAsync(review.Id, ReviewStatus.Pending);

        var mine = await Mine().Handle(new GetMyReviewsQuery(customer, 1, 20), CancellationToken.None);

        var item = mine.Value.Items.Should().ContainSingle().Subject;
        item.Id.Should().Be(review.Id);
        item.Status.Should().Be("Pending");
    }

    [SkippableFact]
    public async Task An_admin_can_republish_a_review_it_took_down()
    {
        var customer = await SeedCustomerAsync();
        var product = await SeedProductAsync();
        var review = await SubmitAsync(customer, product, 5, null, "Back again");

        await ModerateAsync(review.Id, ReviewStatus.Rejected, "Mistake");
        (await AggregateAsync(product)).Should().Be((0m, 0));

        await ModerateAsync(review.Id, ReviewStatus.Published);

        (await Storefront().Handle(new GetProductReviewsQuery(product, 1, 20), CancellationToken.None))
            .Value.Page.Items.Should().ContainSingle();
        (await AggregateAsync(product)).Should().Be((5.00m, 1));
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task<MyReviewResponse> SubmitAsync(
        Guid customer, Guid product, int rating, string? title, string body)
    {
        await using var ctx = Db.CreateDbContext();

        var result = await new SubmitReviewHandler(
                ctx,
                new ProductReviewRatingRecalculator(ctx),
                Cache(),
                NullLogger<SubmitReviewHandler>.Instance)
            .Handle(
                new SubmitReviewCommand(customer, product, null, rating, title, body, null),
                CancellationToken.None);

        result.IsSuccess.Should().BeTrue(because: result.Error?.Description);
        return result.Value;
    }

    private async Task<Result<AdminReviewResponse>> ModerateAsync(
        Guid reviewId, ReviewStatus status, string? reason = null)
    {
        await using var ctx = Db.CreateDbContext();

        return await new ModerateReviewHandler(
                ctx,
                new ProductReviewRatingRecalculator(ctx),
                Cache(),
                NullLogger<ModerateReviewHandler>.Instance)
            .Handle(new ModerateReviewCommand(reviewId, status, reason), CancellationToken.None);
    }

    private async Task<Result> DeleteAsync(Guid reviewId)
    {
        await using var ctx = Db.CreateDbContext();

        return await new AdminDeleteReviewHandler(
                ctx,
                new ProductReviewRatingRecalculator(ctx),
                Cache(),
                NullLogger<AdminDeleteReviewHandler>.Instance)
            .Handle(new AdminDeleteReviewCommand(reviewId), CancellationToken.None);
    }

    private async Task<(decimal Average, int Count)> AggregateAsync(Guid productId)
    {
        await using var ctx = Db.CreateDbContext();
        var product = await ctx.Products.AsNoTracking().FirstAsync(p => p.Id == productId);
        return (product.RatingAverage, product.RatingCount);
    }

    /// <summary>A fresh cache per call, so each read queries the database rather than a warm entry.</summary>
    private GetProductReviewsHandler Storefront()
        => new(Db.CreateDbContext(), new MemoryCache(new MemoryCacheOptions()),
               Options.Create(new CatalogCacheOptions { DefaultExpiryMinutes = 10 }));

    private GetMyReviewsHandler Mine() => new(Db.CreateDbContext());

    /// <summary>
    /// Invalidation is an Application-layer concern already asserted by the unit tests with a
    /// verifying mock. This suite is about what the database returns, so the handlers get a stub
    /// rather than a hand-written fake that could disagree with the real cache service.
    /// </summary>
    private static ICatalogCacheService Cache() => new Mock<ICatalogCacheService>().Object;

    private async Task<Guid> SeedCustomerAsync()
    {
        await using var ctx = Db.CreateDbContext();
        var suffix = Guid.NewGuid().ToString("N")[..10];

        var user = User.CreateCustomer(
            $"pub-{suffix}@example.test", "hash", "Pub", suffix);

        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return user.Id;
    }

    private async Task<Guid> SeedProductAsync()
    {
        await using var ctx = Db.CreateDbContext();
        var product = Product.Create(
            $"Auto {Guid.NewGuid():N}"[..20], $"auto-{Guid.NewGuid():N}", null,
            99m, null, null);
        product.Publish();
        ctx.Products.Add(product);
        await ctx.SaveChangesAsync();
        return product.Id;
    }
}
