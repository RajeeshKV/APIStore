using FluentAssertions;
using KromicCommerce.Application.Caching;
using KromicCommerce.Application.Features.Catalog.Products;
using KromicCommerce.Application.Features.Catalog.Reviews;
using KromicCommerce.Domain.Catalog;
using KromicCommerce.Domain.Identity;
using KromicCommerce.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using KromicCommerce.IntegrationTests.Infrastructure;

namespace KromicCommerce.IntegrationTests.Catalog;

/// <summary>
/// The rating summary is denormalised onto the product row, so exposing it in a product response
/// costs no aggregate query. These tests cover the two things that makes unsafe: that the response
/// actually carries the stored value, and that a cached product page cannot survive a rating change.
/// </summary>
/// <remarks>
/// The recalculation of the stored aggregate is already covered by ReviewIntegrationTests. What
/// those tests do not cover is whether anything reads it back out, or whether the cached product
/// page is invalidated when it moves.
/// </remarks>
[Collection("Database")]
public sealed class ProductRatingExposureTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    // -----------------------------------------------------------------------
    // The response carries the value
    // -----------------------------------------------------------------------

    [SkippableFact]
    public async Task The_admin_product_response_carries_the_stored_rating_summary()
    {
        var productId = await SeedRatedProductAsync(5, 4);

        await using var ctx = Db.CreateDbContext();
        var product = await ctx.Products
            .AsNoTracking()
            .Include(p => p.Images)
            .Include(p => p.Variants)
            .Include(p => p.Attributes)
            .FirstAsync(p => p.Id == productId);

        var response = ProductMapper.MapToResponse(product);

        response.RatingCount.Should().Be(2);
        response.RatingAverage.Should().Be(4.50m);
        response.HasRatings.Should().BeTrue();
    }

    [SkippableFact]
    public async Task An_unrated_product_reports_no_ratings_rather_than_a_zero_score()
    {
        var productId = await SeedProductAsync();

        await using var ctx = Db.CreateDbContext();
        var product = await ctx.Products.AsNoTracking()
            .Include(p => p.Images).Include(p => p.Variants).Include(p => p.Attributes)
            .FirstAsync(p => p.Id == productId);

        var response = ProductMapper.MapToResponse(product);

        response.RatingCount.Should().Be(0);
        // Zero is what the column stores, but HasRatings is what callers must branch on — a
        // client that renders "0.0 stars" for an unreviewed product is showing a score nobody gave.
        response.HasRatings.Should().BeFalse();
        response.RatingAverage.Should().Be(0m);
    }

    [Fact]
    public void HasRatings_is_derived_from_the_count_so_the_two_cannot_disagree()
    {
        var rated = new Contracts.Catalog.StorefrontProductSummaryResponse(
            Guid.NewGuid(), "n", "s", null, 1m, null, "INR", null,
            Contracts.Catalog.StockAvailability.InStock, true,
            null, null, null, null, null, null, false, 4.5m, 1);

        rated.HasRatings.Should().BeTrue();

        // Derived, never stored separately, so it cannot drift from RatingCount.
        rated.HasRatings.Should().Be(rated.RatingCount > 0);
    }

    // -----------------------------------------------------------------------
    // A cached product page cannot outlive a rating change
    // -----------------------------------------------------------------------

    [SkippableFact]
    public async Task Changing_the_rating_summary_invalidates_the_cached_product_page()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 2048 });
        var productId = await SeedProductAsync();

        // A rating arrives: the handler recalculates the aggregate and saves the product, so the
        // cached page must be evicted even though no ProductReview row was tracked by the context
        // that submitted it.
        await PublishAsync(productId, 5);
        await RecalculateAsync(productId);

        await using var ctx = Db.CreateCacheObservedDbContext(memoryCache, out var recorder);
        var product = await ctx.Products.FirstAsync(p => p.Id == productId);
        product.SetRatingAggregate(
            ReviewRatingAggregate.FromRatings([5, 4]));
        await ctx.SaveChangesAsync();

        recorder.Called("InvalidateStorefrontProduct")
            .Should().BeTrue(
                "the product page embeds the rating summary, so a changed aggregate must not be " +
                "served from a cache entry written before it");
        recorder.Called("InvalidateStorefrontFeatured")
            .Should().BeTrue("the featured list embeds the same rating summary");
    }

    [SkippableFact]
    public async Task A_review_write_that_only_touches_the_review_row_still_leaves_the_page_alone()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 2048 });
        var productId = await SeedRatedProductAsync(5, 4);

        await using var ctx = Db.CreateCacheObservedDbContext(memoryCache, out var recorder);
        var review = await ctx.ProductReviews.FirstAsync(r => r.ProductId == productId);
        ctx.ReviewHelpfulVotes.Add(ReviewHelpfulVote.Create(review.Id, Guid.NewGuid()));
        await ctx.SaveChangesAsync();

        // A helpful vote changes no rating, so the product page stays valid and the review pages
        // are what need evicting. Over-invalidating the page here would cost cache hit rate for
        // no correctness gain.
        recorder.Called("InvalidateProductReviews")
            .Should().BeTrue("helpful votes reorder the review list");
        recorder.Called("InvalidateStorefrontProduct")
            .Should().BeFalse("a helpful vote does not change the stored rating summary");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task<Guid> SeedProductAsync()
    {
        await using var ctx = Db.CreateDbContext();
        var product = Product.Create(
            "Rated Product", $"rated-{Guid.NewGuid():N}", $"SKU-{Guid.NewGuid():N}", 10m, null, null);
        ctx.Products.Add(product);
        await ctx.SaveChangesAsync();
        return product.Id;
    }

    /// <summary>Seeds a product whose aggregate reflects the supplied published ratings.</summary>
    private async Task<Guid> SeedRatedProductAsync(params int[] ratings)
    {
        var productId = await SeedProductAsync();

        foreach (var rating in ratings)
            await PublishAsync(productId, rating);

        await RecalculateAsync(productId);
        return productId;
    }

    private async Task PublishAsync(Guid productId, int rating)
    {
        await using var ctx = Db.CreateDbContext();
        var customer = await ctx.Users.FirstOrDefaultAsync()
            ?? CreateCustomerEntity(ctx);

        await ctx.TryAddProductReviewAsync(
            customer.Id, productId, null, rating, "Great", "Body", false, ReviewStatus.Published);
    }

    private static User CreateCustomerEntity(AppDbContext ctx)
    {
        var user = User.CreateCustomer(
            $"rated-{Guid.NewGuid():N}@example.test", null, "rated-tester", null);
        ctx.Users.Add(user);
        ctx.CustomerProfiles.Add(CustomerProfile.Create(user.Id));
        return user;
    }

    private async Task RecalculateAsync(Guid productId)
    {
        await using var ctx = Db.CreateDbContext();
        await new ProductReviewRatingRecalculator(ctx)
            .RecalculateAsync(productId, CancellationToken.None);
        await ctx.SaveChangesAsync();
    }
}