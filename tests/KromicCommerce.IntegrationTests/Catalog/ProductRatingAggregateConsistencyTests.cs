using KromicCommerce.Application.Features.Catalog.Reviews;
using KromicCommerce.Domain.Catalog;
using KromicCommerce.Domain.Identity;
using KromicCommerce.Infrastructure.Persistence;
using KromicCommerce.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KromicCommerce.IntegrationTests.Catalog;

/// <summary>
/// Pins the ordering contract between a review mutation and the recalculation of the product's
/// stored rating summary.
/// </summary>
/// <remarks>
/// <para>
/// Every review handler mutates the review and recalculates the aggregate <em>inside one unit of
/// work</em>, saving once at the end. That is the only arrangement that keeps the review rows and
/// the number that summarises them atomic — and it is the arrangement that broke: a LINQ query
/// goes to PostgreSQL and cannot see the change tracker, so the recalculator was reading the rows
/// as they were <em>before</em> the change it was about to save.
/// </para>
///
/// <para>
/// The drift looked like a cache bug and was not one. In production a product listed
/// <c>ratingCount: 1</c> while its own reviews endpoint reported two published reviews, because
/// moderating a review Pending → Published recomputed the average from a set that still excluded
/// the review being published. The list endpoint is not cached at all, so no amount of cache
/// invalidation could have corrected it.
/// </para>
///
/// <para>
/// The existing <c>ReviewIntegrationTests</c> could not catch this: its helpers recalculate from a
/// <em>fresh</em> context after the write has already committed, so the database is consistent by
/// then and the ordering bug is invisible. These tests reproduce the handler ordering instead.
/// </para>
/// </remarks>
[Collection("Database")]
public sealed class ProductRatingAggregateConsistencyTests
{
    private readonly DatabaseFixture Db;

    public ProductRatingAggregateConsistencyTests(DatabaseFixture db) => Db = db;

    // -----------------------------------------------------------------------
    // The production sequence: submit two reviews, publish both.
    // -----------------------------------------------------------------------

    [SkippableFact]
    public async Task Publishing_a_review_updates_the_stored_count_in_the_same_unit_of_work()
    {
        var author = await SeedCustomerAsync();
        var product = await SeedProductAsync();

        await PublishInOneUnitOfWorkAsync(author, product, 5);
        (await AggregateAsync(product)).Should().Be((5.00m, 1));

        // The regression: this second publish left the stored count at 1 while two reviews were
        // published, because the recalculation read the row before its own status change.
        var second = await SeedCustomerAsync();
        await PublishInOneUnitOfWorkAsync(second, product, 5);

        (await AggregateAsync(product)).Should().Be((5.00m, 2),
            "a review that becomes published in this save must be counted by the aggregate it is " +
            "saved with; reading the pre-change rows leaves the count permanently one behind");
    }

    [SkippableFact]
    public async Task Unpublishing_a_review_lowers_the_stored_count_in_the_same_unit_of_work()
    {
        var author = await SeedCustomerAsync();
        var product = await SeedProductAsync();

        await PublishInOneUnitOfWorkAsync(author, product, 5);
        (await AggregateAsync(product)).Should().Be((5.00m, 1));

        await ModerateInOneUnitOfWorkAsync(author, product, ReviewStatus.Rejected, "removed");

        (await AggregateAsync(product)).Should().Be((0m, 0),
            "a review leaving the published set in this save must stop being counted");
    }

    [SkippableFact]
    public async Task Editing_the_rating_of_a_published_review_recomputes_from_the_new_value()
    {
        var author = await SeedCustomerAsync();
        var product = await SeedProductAsync();

        await PublishInOneUnitOfWorkAsync(author, product, 5);
        (await AggregateAsync(product)).Should().Be((5.00m, 1));

        // 5 -> 1 must move the average to 1.00. Reading the committed rows instead of the tracked
        // entity would keep reporting 5.00.
        await EditInOneUnitOfWorkAsync(author, product, 1);

        (await AggregateAsync(product)).Should().Be((1.00m, 1));
    }

    [SkippableFact]
    public async Task Deleting_a_published_review_lowers_the_stored_count_in_the_same_unit_of_work()
    {
        var author = await SeedCustomerAsync();
        var product = await SeedProductAsync();

        await PublishInOneUnitOfWorkAsync(author, product, 5);
        await DeleteInOneUnitOfWorkAsync(author, product);

        (await AggregateAsync(product)).Should().Be((0m, 0),
            "the delete is still only pending when the recalculation runs, so the row has to be " +
            "removed from the set explicitly or the count can never fall");
    }

    // -----------------------------------------------------------------------
    // Consistency between the two surfaces the customer sees
    // -----------------------------------------------------------------------

    /// <summary>
    /// The symptom as reported: the product response and the reviews response disagree about how
    /// many reviews exist.
    /// </summary>
    [SkippableFact]
    public async Task The_stored_summary_agrees_with_the_live_review_count()
    {
        var a = await SeedCustomerAsync();
        var b = await SeedCustomerAsync();
        var product = await SeedProductAsync();

        await PublishInOneUnitOfWorkAsync(a, product, 5);
        await PublishInOneUnitOfWorkAsync(b, product, 4);

        var stored = await AggregateAsync(product);

        await using var verify = NewContext();
        var live = await verify.ProductReviews
            .AsNoTracking()
            .CountAsync(r => r.ProductId == product && r.Status == ReviewStatus.Published);

        stored.Count.Should().Be(live);
        stored.Average.Should().Be(4.50m);
    }

    // -----------------------------------------------------------------------
    // Helpers — each reproduces one handler's ordering exactly
    // -----------------------------------------------------------------------

    private AppDbContext NewContext() => Db.CreateDbContext();

    /// <summary>Mirrors <c>ModerateReviewHandler</c>: set the status, recalculate, save once.</summary>
    private async Task PublishInOneUnitOfWorkAsync(Guid customer, Guid product, int rating)
    {
        await using var ctx = NewContext();

        await ctx.TryAddProductReviewAsync(
            customer, product, null, rating, "Great", "Body", false, ReviewStatus.Pending);

        var review = await ctx.ProductReviews
            .FirstAsync(r => r.CustomerId == customer && r.ProductId == product);

        review.SetStatus(ReviewStatus.Published, null);

        await new ProductReviewRatingRecalculator(ctx).RecalculateAsync(product, CancellationToken.None);
        await ctx.SaveChangesAsync();
    }

    private async Task ModerateInOneUnitOfWorkAsync(
        Guid customer, Guid product, ReviewStatus status, string? reason)
    {
        await using var ctx = NewContext();

        var review = await ctx.ProductReviews
            .FirstAsync(r => r.CustomerId == customer && r.ProductId == product);

        review.SetStatus(status, reason);

        await new ProductReviewRatingRecalculator(ctx).RecalculateAsync(product, CancellationToken.None);
        await ctx.SaveChangesAsync();
    }

    private async Task EditInOneUnitOfWorkAsync(Guid customer, Guid product, int rating)
    {
        await using var ctx = NewContext();

        var review = await ctx.ProductReviews
            .FirstAsync(r => r.CustomerId == customer && r.ProductId == product);

        review.Edit(rating, "Great", "Body");

        await new ProductReviewRatingRecalculator(ctx).RecalculateAsync(product, CancellationToken.None);
        await ctx.SaveChangesAsync();
    }

    private async Task DeleteInOneUnitOfWorkAsync(Guid customer, Guid product)
    {
        await using var ctx = NewContext();

        var review = await ctx.ProductReviews
            .FirstAsync(r => r.CustomerId == customer && r.ProductId == product);

        review.RaiseDeleted();
        ctx.ProductReviews.Remove(review);

        await new ProductReviewRatingRecalculator(ctx).RecalculateAsync(product, CancellationToken.None);
        await ctx.SaveChangesAsync();
    }

    private async Task<(decimal Average, int Count)> AggregateAsync(Guid productId)
    {
        await using var ctx = NewContext();
        var product = await ctx.Products.AsNoTracking().FirstAsync(p => p.Id == productId);
        return (product.RatingAverage, product.RatingCount);
    }

    private async Task<Guid> SeedCustomerAsync()
    {
        await using var ctx = NewContext();
        var suffix = Guid.NewGuid().ToString("N")[..10];

        var user = User.CreateCustomer(
            $"agg-{suffix}@example.test", "hash", "Agg", suffix);

        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        return user.Id;
    }

    private async Task<Guid> SeedProductAsync()
    {
        await using var ctx = NewContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var product = Product.Create(
            $"Aggregate {suffix}", $"aggregate-{suffix}", null, 10m, null, null);

        ctx.Products.Add(product);
        await ctx.SaveChangesAsync();

        return product.Id;
    }
}
