using KromicCommerce.Application.Abstractions.Catalog;
using KromicCommerce.Application.Caching;
using KromicCommerce.Application.Features.Catalog.Reviews;
using KromicCommerce.Application.Options;
using KromicCommerce.Contracts.Catalog;
using KromicCommerce.Domain.Catalog;
using KromicCommerce.Domain.Identity;
using KromicCommerce.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace KromicCommerce.IntegrationTests.Catalog;

/// <summary>
/// Review and wishlist behaviour against a real PostgreSQL database.
///
/// These exist because the guarantees under test are database guarantees. A unit test with an
/// in-memory provider cannot observe a unique index, so "one review per customer per product" and
/// "a NULL variant is a single key" would both look fine in unit tests while being broken in
/// production. The concurrency tests likewise prove the ON CONFLICT path, not a handler's
/// pre-check.
/// </summary>
[Collection("Database")]
public sealed class ReviewIntegrationTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    private const string CloudinaryUrl = "https://res.cloudinary.com/demo/image/upload/reviews/x.jpg";

    // -----------------------------------------------------------------------
    // NULLS NOT DISTINCT — the reason this index was hand-authored
    // -----------------------------------------------------------------------

    [SkippableFact]
    public async Task A_second_product_level_review_with_a_null_variant_is_rejected()
    {
        var customer = await SeedCustomerAsync();
        var product = await SeedProductAsync();

        await using (var ctx = Db.CreateDbContext())
        {
            var created = await ctx.TryAddProductReviewAsync(
                customer, product, null, 5, "First", "Body", false, ReviewStatus.Pending);
            created.Should().BeTrue();
        }

        // ON CONFLICT DO NOTHING must not swallow this — if the index lacked
        // NULLS NOT DISTINCT, this would silently succeed and create a duplicate row.
        await using (var ctx = Db.CreateDbContext())
        {
            var created = await ctx.TryAddProductReviewAsync(
                customer, product, null, 1, "Second", "Body", false, ReviewStatus.Pending);
            created.Should().BeFalse(
                "a NULL ProductVariantId must be a single key, otherwise one customer can post " +
                "unlimited product-level reviews");
        }

        await using var verify = Db.CreateDbContext();
        (await verify.ProductReviews.CountAsync(r =>
            r.CustomerId == customer && r.ProductId == product)).Should().Be(1);
    }

    [SkippableFact]
    public async Task A_direct_insert_of_a_duplicate_null_variant_review_raises_a_unique_violation()
    {
        // The handler path above goes through ON CONFLICT DO NOTHING, which cannot raise. This
        // proves the constraint itself exists, so the guard cannot be lost by regenerating the
        // migration without the NULLS NOT DISTINCT clause.
        var customer = await SeedCustomerAsync();
        var product = await SeedProductAsync();
        var now = DateTime.UtcNow;

        await using var ctx = Db.CreateDbContext();
        ctx.Database.ExecuteSqlRaw(
            @"INSERT INTO product_reviews (""Id"",""CustomerId"",""ProductId"",""ProductVariantId"",""Rating"",""Body"",""Status"",""HelpfulCount"",""CreatedAtUtc"",""UpdatedAtUtc"")
              VALUES ({0},{1},{2},NULL,5,'first','Pending',0,{3},{3})",
            Guid.NewGuid(), customer, product, now);
        await ctx.SaveChangesAsync();

        var act = () => ctx.Database.ExecuteSqlRaw(
            @"INSERT INTO product_reviews (""Id"",""CustomerId"",""ProductId"",""ProductVariantId"",""Rating"",""Body"",""Status"",""HelpfulCount"",""CreatedAtUtc"",""UpdatedAtUtc"")
              VALUES ({0},{1},{2},NULL,5,'second','Pending',0,{3},{3})",
            Guid.NewGuid(), customer, product, now);

        // ExecuteSqlRaw bypasses the change tracker, so Npgsql's own PostgresException surfaces
        // unwrapped. 23505 is unique_violation.
        act.Should().Throw<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.UniqueViolation,
                "the unique index must reject a duplicate NULL-variant review at the database level");
    }

    [SkippableFact]
    public async Task Product_level_and_variant_specific_reviews_can_coexist()
    {
        // The mirror image of the test above: NULL and a real variant are different keys, so a
        // customer may review both the product generally and one variant specifically.
        var customer = await SeedCustomerAsync();
        var product = await SeedProductAsync();
        var variant = await SeedVariantAsync(product);

        await using var ctx = Db.CreateDbContext();
        (await ctx.TryAddProductReviewAsync(
            customer, product, null, 5, "General", "Body", false, ReviewStatus.Pending))
            .Should().BeTrue();
        (await ctx.TryAddProductReviewAsync(
            customer, product, variant, 3, "Variant", "Body", false, ReviewStatus.Pending))
            .Should().BeTrue();

        (await ctx.ProductReviews.CountAsync(r => r.CustomerId == customer)).Should().Be(2);
    }

    [SkippableFact]
    public async Task Concurrent_submits_from_one_customer_produce_exactly_one_review()
    {
        var customer = await SeedCustomerAsync();
        var product = await SeedProductAsync();

        // Eight contexts racing the same insert. Exactly one may win; the rest must be told they
        // lost rather than raising. This is what the unique index is for — a handler pre-check
        // cannot make this guarantee.
        var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var ctx = Db.CreateDbContext();
            return await ctx.TryAddProductReviewAsync(
                customer, product, null, 4, "Racing", "Body", false, ReviewStatus.Pending);
        }));

        attempts.Count(won => won).Should().Be(1);

        await using var verify = Db.CreateDbContext();
        (await verify.ProductReviews.CountAsync(r =>
            r.CustomerId == customer && r.ProductId == product)).Should().Be(1);
    }

    [SkippableFact]
    public async Task Concurrent_wishlist_adds_produce_exactly_one_entry()
    {
        var customer = await SeedCustomerAsync();
        var product = await SeedProductAsync();

        var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var ctx = Db.CreateDbContext();
            return await ctx.TryAddWishlistItemAsync(customer, product, null);
        }));

        attempts.Count(won => won).Should().Be(1);

        await using var verify = Db.CreateDbContext();
        (await verify.WishlistItems.CountAsync(w =>
            w.CustomerId == customer && w.ProductId == product)).Should().Be(1);
    }

    [SkippableFact]
    public async Task A_rating_outside_one_to_five_is_rejected_by_the_database()
    {
        // Defence in depth behind the domain guard: proves the CHECK constraint survived the
        // migration, so a code path that skips ProductReview.Create cannot store a 0 or 6.
        var customer = await SeedCustomerAsync();
        var product = await SeedProductAsync();
        var now = DateTime.UtcNow;

        await using var ctx = Db.CreateDbContext();
        var act = () => ctx.Database.ExecuteSqlRaw(
            @"INSERT INTO product_reviews (""Id"",""CustomerId"",""ProductId"",""ProductVariantId"",""Rating"",""Body"",""Status"",""HelpfulCount"",""CreatedAtUtc"",""UpdatedAtUtc"")
              VALUES ({0},{1},{2},NULL,6,'out of range','Pending',0,{3},{3})",
            Guid.NewGuid(), customer, product, now);

        // ExecuteSqlRaw bypasses the change tracker, so Npgsql's own PostgresException surfaces
        // unwrapped rather than as a DbUpdateException. 23514 is check_violation.
        act.Should().Throw<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.CheckViolation,
                "the CHECK constraint must reject a rating outside 1..5 even when the domain " +
                "guard is bypassed");
    }

    // -----------------------------------------------------------------------
    // Cascade behaviour
    // -----------------------------------------------------------------------

    [SkippableFact]
    public async Task Deleting_a_product_cascades_to_reviews_images_and_votes()
    {
        var customer = await SeedCustomerAsync();
        var voter = await SeedCustomerAsync();
        var product = await SeedProductAsync();

        Guid reviewId;
        await using (var ctx = Db.CreateDbContext())
        {
            var review = ProductReview.Create(
                customer, product, null, 5, "Great", "Body", true, ReviewStatus.Published);
            review.AddImage(MediaAsset.Create("reviews/a", CloudinaryUrl, "jpg", 10, 10, null), 0);
            ctx.ProductReviews.Add(review);
            await ctx.SaveChangesAsync();
            reviewId = review.Id;
        }

        await using (var ctx = Db.CreateDbContext())
        {
            ctx.ReviewHelpfulVotes.Add(ReviewHelpfulVote.Create(reviewId, voter));
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = Db.CreateDbContext())
        {
            await ctx.Products.Where(p => p.Id == product).ExecuteDeleteAsync();
        }

        await using var verify = Db.CreateDbContext();
        (await verify.ProductReviews.CountAsync(r => r.ProductId == product)).Should().Be(0);
        (await verify.ReviewHelpfulVotes.CountAsync(v => v.ReviewId == reviewId)).Should().Be(0);
        (await verify.Set<ReviewImage>()
            .FromSqlRaw("SELECT * FROM review_images WHERE \"ReviewId\" = {0}", reviewId)
            .ToListAsync()).Should().BeEmpty();
    }

    [SkippableFact]
    public async Task Deleting_a_customer_cascades_to_their_reviews_and_wishlist()
    {
        var customer = await SeedCustomerAsync();
        var product = await SeedProductAsync();

        await using (var ctx = Db.CreateDbContext())
        {
            await ctx.TryAddProductReviewAsync(
                customer, product, null, 4, "Mine", "Body", false, ReviewStatus.Published);
            await ctx.TryAddWishlistItemAsync(customer, product, null);
        }

        await using (var ctx = Db.CreateDbContext())
            await ctx.Users.Where(u => u.Id == customer).ExecuteDeleteAsync();

        await using var verify = Db.CreateDbContext();
        (await verify.ProductReviews.CountAsync(r => r.CustomerId == customer)).Should().Be(0);
        (await verify.WishlistItems.CountAsync(w => w.CustomerId == customer)).Should().Be(0);
    }

    [SkippableFact]
    public async Task Deleting_a_variant_nulls_the_review_variant_but_keeps_the_review()
    {
        // The FK is SET NULL, not CASCADE. Cascading would delete a customer's review because an
        // admin removed a size option — destroying content over a catalog edit.
        var customer = await SeedCustomerAsync();
        var product = await SeedProductAsync();
        var variant = await SeedVariantAsync(product);

        Guid reviewId;
        await using (var ctx = Db.CreateDbContext())
        {
            await ctx.TryAddProductReviewAsync(
                customer, product, variant, 4, "Variant", "Body", false, ReviewStatus.Published);
            reviewId = await ctx.ProductReviews
                .Where(r => r.CustomerId == customer)
                .Select(r => r.Id)
                .FirstAsync();
        }

        await using (var ctx = Db.CreateDbContext())
            await ctx.ProductVariants.Where(v => v.Id == variant).ExecuteDeleteAsync();

        await using var verify = Db.CreateDbContext();
        var review = await verify.ProductReviews.FirstAsync(r => r.Id == reviewId);
        review.ProductVariantId.Should().BeNull();
        review.Body.Should().Be("Body");
    }

    // -----------------------------------------------------------------------
    // Rating aggregate
    // -----------------------------------------------------------------------

    [SkippableFact]
    public async Task Moderation_recalculates_the_stored_aggregate_to_match_the_published_set()
    {
        var a = await SeedCustomerAsync();
        var b = await SeedCustomerAsync();
        var c = await SeedCustomerAsync();
        var product = await SeedProductAsync();

        await PublishAsync(a, product, 5);
        await PublishAsync(b, product, 4);
        await PublishAsync(c, product, 3);
        await RecalculateAsync(product);

        // (5 + 4 + 3) / 3 = 4.00
        (await AggregateAsync(product)).Should().Be((4.00m, 3));

        // Unpublishing one must change the stored value, not just the visible list.
        await SetStatusAsync(a, product, ReviewStatus.Rejected, "removed");
        await RecalculateAsync(product);

        // (4 + 3) / 2 = 3.50
        (await AggregateAsync(product)).Should().Be((3.50m, 2));
    }

    [SkippableFact]
    public async Task Removing_the_last_published_review_resets_the_aggregate_to_zero()
    {
        // The drift case an increment-only counter gets wrong: it would leave the last average
        // and count in place forever.
        var a = await SeedCustomerAsync();
        var product = await SeedProductAsync();

        await PublishAsync(a, product, 5);
        await RecalculateAsync(product);
        (await AggregateAsync(product)).Should().Be((5.00m, 1));

        await using (var ctx = Db.CreateDbContext())
            await ctx.ProductReviews.Where(r => r.ProductId == product).ExecuteDeleteAsync();

        await RecalculateAsync(product);

        (await AggregateAsync(product)).Should().Be((0m, 0));
    }

    [SkippableFact]
    public async Task Withdrawing_the_last_helpful_vote_resets_the_count_to_zero()
    {
        var author = await SeedCustomerAsync();
        var voter = await SeedCustomerAsync();
        var product = await SeedProductAsync();
        var reviewId = await PublishAsync(author, product, 4);

        await using (var ctx = Db.CreateDbContext())
        {
            ctx.ReviewHelpfulVotes.Add(ReviewHelpfulVote.Create(reviewId, voter));
            await ctx.SaveChangesAsync();
        }

        await RecalculateHelpfulAsync(reviewId);
        (await HelpfulCountAsync(reviewId)).Should().Be(1);

        await using (var ctx = Db.CreateDbContext())
            await ctx.ReviewHelpfulVotes.Where(v => v.ReviewId == reviewId).ExecuteDeleteAsync();

        await RecalculateHelpfulAsync(reviewId);

        (await HelpfulCountAsync(reviewId)).Should().Be(0,
            "an increment-only counter can never fall back to zero");
    }

    // -----------------------------------------------------------------------
    // Public listing
    // -----------------------------------------------------------------------

    [SkippableFact]
    public async Task Public_listing_excludes_unpublished_reviews()
    {
        var customer = await SeedCustomerAsync();
        var product = await SeedProductAsync();

        await PublishAsync(customer, product, 5);
        await using (var ctx = Db.CreateDbContext())
        {
            await ctx.TryAddProductReviewAsync(
                customer, product, null, 1, "Pending", "Body", false, ReviewStatus.Pending);
        }

        var result = await Storefront().Handle(
            new GetProductReviewsQuery(product, 1, 20), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Page.Items.Should().HaveCount(1);
        result.Value.Page.Items[0].Title.Should().Be("Great");
        result.Value.RatingCount.Should().Be(1);
    }

    [SkippableFact]
    public async Task Public_listing_hides_the_authors_identity()
    {
        var customer = await SeedCustomerAsync(signature: "Ada");
        var product = await SeedProductAsync();
        await PublishAsync(customer, product, 5);

        var result = await Storefront().Handle(
            new GetProductReviewsQuery(product, 1, 20), CancellationToken.None);

        var item = result.Value.Page.Items.Should().ContainSingle().Subject;
        // First + last name, falling back to the email local part. Never the raw email or any id.
        item.AuthorName.Should().Be("Ada Tester");
        // The public projection has no customer id, email, or phone field at all.
        typeof(ProductReviewSummaryResponse)
            .GetProperties()
            .Select(p => p.Name)
            .Should().NotContain(new[] { "CustomerId", "CustomerEmail", "Email", "Phone" });
    }

    [SkippableFact]
    public async Task Public_ordering_by_rating_is_stable_when_ratings_tie()
    {
        var product = await SeedProductAsync();
        for (var i = 0; i < 6; i++)
            await PublishAsync(await SeedCustomerAsync(), product, 3);

        var first = await Storefront().Handle(
            new GetProductReviewsQuery(product, 1, 20, "rating"), CancellationToken.None);
        var again = await Storefront().Handle(
            new GetProductReviewsQuery(product, 1, 20, "rating"), CancellationToken.None);

        first.Value.Page.Items.Select(i => i.Id)
            .Should().Equal(again.Value.Page.Items.Select(i => i.Id),
                "tied sort keys must fall back to publish time then id, not to whatever order " +
                "PostgreSQL happens to return");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task<Guid> PublishAsync(Guid customer, Guid product, int rating)
    {
        await using var ctx = Db.CreateDbContext();
        await ctx.TryAddProductReviewAsync(
            customer, product, null, rating, "Great", "Body", false, ReviewStatus.Published);

        return await ctx.ProductReviews
            .Where(r => r.CustomerId == customer && r.ProductId == product)
            .Select(r => r.Id)
            .FirstAsync();
    }

    private async Task SetStatusAsync(Guid customer, Guid product, ReviewStatus status, string? reason)
    {
        await using var ctx = Db.CreateDbContext();
        var review = await ctx.ProductReviews
            .FirstAsync(r => r.CustomerId == customer && r.ProductId == product);
        review.SetStatus(status, reason);
        await ctx.SaveChangesAsync();
    }

    private async Task RecalculateAsync(Guid productId)
    {
        await using var ctx = Db.CreateDbContext();
        await new ProductReviewRatingRecalculator(ctx).RecalculateAsync(productId, CancellationToken.None);
        await ctx.SaveChangesAsync();
    }

    private async Task RecalculateHelpfulAsync(Guid reviewId)
    {
        await using var ctx = Db.CreateDbContext();
        await new ProductReviewRatingRecalculator(ctx)
            .RecalculateHelpfulCountsAsync([reviewId], CancellationToken.None);
        await ctx.SaveChangesAsync();
    }

    private async Task<(decimal Average, int Count)> AggregateAsync(Guid productId)
    {
        await using var ctx = Db.CreateDbContext();
        var product = await ctx.Products.AsNoTracking().FirstAsync(p => p.Id == productId);
        return (product.RatingAverage, product.RatingCount);
    }

    private async Task<int> HelpfulCountAsync(Guid reviewId)
    {
        await using var ctx = Db.CreateDbContext();
        return await ctx.ProductReviews.AsNoTracking()
            .Where(r => r.Id == reviewId)
            .Select(r => r.HelpfulCount)
            .FirstAsync();
    }

    private GetProductReviewsHandler Storefront()
        => new(
            Db.CreateDbContext(),
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new CatalogCacheOptions { DefaultExpiryMinutes = 10 }));

    private async Task<Guid> SeedCustomerAsync(string signature = "")
    {
        await using var ctx = Db.CreateDbContext();
        var suffix = Guid.NewGuid().ToString("N")[..10];

        var user = User.CreateCustomer(
            $"rev-{suffix}@example.test", "hash",
            signature.Length > 0 ? signature : "Review",
            "Tester");

        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return user.Id;
    }

    private async Task<Guid> SeedProductAsync()
    {
        await using var ctx = Db.CreateDbContext();
        var product = Product.Create(
            $"Reviewed {Guid.NewGuid():N}"[..20], $"reviewed-{Guid.NewGuid():N}", null,
            99m, null, null);
        product.Publish();
        ctx.Products.Add(product);
        await ctx.SaveChangesAsync();
        return product.Id;
    }

    private async Task<Guid> SeedVariantAsync(Guid productId)
    {
        await using var ctx = Db.CreateDbContext();
        var variant = ProductVariant.Create(productId, $"sku-{Guid.NewGuid():N}"[..12], null);
        ctx.ProductVariants.Add(variant);
        await ctx.SaveChangesAsync();
        return variant.Id;
    }
}