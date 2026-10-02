using KromicCommerce.Application.Abstractions.Catalog;
using KromicCommerce.Application.Caching;
using KromicCommerce.Application.Options;
using KromicCommerce.Application.Features.Admin.Carousel;
using KromicCommerce.Domain.Catalog;
using KromicCommerce.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KromicCommerce.IntegrationTests.Catalog;

/// <summary>
/// Carousel behaviour against a real PostgreSQL database.
///
/// The unit tests prove the handler composes the right query; these prove the query translates to
/// the SQL the storefront actually needs — in particular that the public filter and the
/// deterministic ordering survive a real sort, where ties are resolved by the database rather than
/// by the in-memory provider the unit tests use.
/// </summary>
[Collection("Database")]
public sealed class CarouselIntegrationTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    private const string ImageUrl = "https://res.cloudinary.com/demo/image/upload/carousel/x.jpg";

    [SkippableFact]
    public async Task Public_carousel_returns_only_active_slides_with_an_image_in_order()
    {
        var tag = Tag();
        await SeedAsync(tag,
            Slide(tag, "visible-1", sortOrder: 1, active: true),
            Slide(tag, "visible-2", sortOrder: 2, active: true),
            Slide(tag, "hidden", sortOrder: 0, active: false),
            Slide(tag, "no-image", sortOrder: 0, active: true, withImage: false));

        var result = await Storefront().Handle(new GetStorefrontCarouselQuery(), CancellationToken.None);

var mine = result.Value.Where(s => TagOf(s.Title) == tag).ToList();

        mine.Select(s => s.Title).Should().ContainInOrder(
            $"{tag}-visible-1", $"{tag}-visible-2");
        mine.Should().NotContain(s => s.Title.Contains("-hidden"));
        // A draft whose upload never happened is never rendered.
        mine.Should().NotContain(s => s.Title.Contains("-no-image"));
    }

[SkippableFact]
    public async Task Public_ordering_is_stable_when_display_orders_tie()
    {
        var tag = Tag();

        // Several slides sharing one display order, each committed separately so their
        // CreatedAtUtc values are strictly increasing and distinct.
        var slides = Enumerable.Range(0, 8)
            .Select(i => Slide(tag, $"tied-{i}", sortOrder: 7, active: true))
            .ToList();
        foreach (var slide in slides) await SeedAsync(tag, slide);

        await using var ctx = Db.CreateDbContext();
        var byId = await ctx.CarouselSlides
            .Where(s => s.Title.StartsWith(tag))
            .ToDictionaryAsync(s => s.Id);

        // The order the tie-breakers must produce: CreatedAtUtc ascending.
        var expected = slides
            .OrderBy(s => byId[s.Id].CreatedAtUtc)
            .Select(s => s.Title)
            .ToList();

        var result = await Storefront().Handle(new GetStorefrontCarouselQuery(), CancellationToken.None);
        var mine = result.Value.Where(s => TagOf(s.Title) == tag).ToList();

        mine.Should().HaveCount(8);
        mine.Select(s => s.Title).Should().Equal(expected,
            "slides sharing a display order must fall back to creation time, not to whatever order " +
            "the database returns");

        // And it must not reshuffle between calls.
        var again = await Storefront().Handle(new GetStorefrontCarouselQuery(), CancellationToken.None);
        again.Value.Where(s => TagOf(s.Title) == tag).Select(s => s.Title).ToList()
            .Should().Equal(expected);
    }

    [SkippableFact]
    public async Task Public_cta_always_points_at_shop()
    {
        var tag = Tag();
        await SeedAsync(tag,
            Slide(tag, "cta-a", sortOrder: 0, active: true, cta: "Shop Now"),
            Slide(tag, "cta-b", sortOrder: 1, active: true, cta: "Explore Collection"));

        var result = await Storefront().Handle(new GetStorefrontCarouselQuery(), CancellationToken.None);
        var mine = result.Value.Where(s => TagOf(s.Title) == tag).ToList();

        mine.Should().HaveCount(2);
        mine.Should().OnlyContain(s => s.CtaTarget == "/shop");
        // The label stays per-slide, so the button copy remains admin-configurable.
        mine.Select(s => s.CtaText).Should().Contain("Shop Now")
            .And.Contain("Explore Collection");
    }

    [SkippableFact]
    public async Task Empty_carousel_returns_an_empty_list_not_an_error()
    {
        var tag = Tag();
        await SeedAsync(tag, Slide(tag, "only", sortOrder: 0, active: false));

        var result = await Storefront().Handle(new GetStorefrontCarouselQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Where(s => TagOf(s.Title) == tag).Should().BeEmpty();
    }

    [SkippableFact]
    public async Task Admin_list_sees_inactive_slides_that_the_storefront_hides()
    {
        var tag = Tag();
        var hidden = Slide(tag, "hidden", sortOrder: 1, active: false);
        await SeedAsync(tag, hidden);

        var admin = await new GetAdminCarouselSlidesHandler(Context())
            .Handle(new GetAdminCarouselSlidesQuery(false), CancellationToken.None);
        var activeOnly = await new GetAdminCarouselSlidesHandler(Context())
            .Handle(new GetAdminCarouselSlidesQuery(true), CancellationToken.None);

        admin.Value.Should().Contain(s => s.Title.Contains(tag));
        activeOnly.Value.Should().NotContain(s => s.Title.Contains(tag));
    }

    [SkippableFact]
    public async Task Deactivating_a_slide_removes_it_from_the_storefront()
    {
        var tag = Tag();
        var slide = Slide(tag, "toggle", sortOrder: 1, active: true);
        await SeedAsync(tag, slide);

        (await Storefront().Handle(new GetStorefrontCarouselQuery(), CancellationToken.None))
            .Value.Should().Contain(s => s.Title.Contains(tag));

        await using (var ctx = Db.CreateDbContext())
        {
            var handler = new UpdateCarouselSlideHandler(
                ctx, new NoOpCacheService(), NullLogger<UpdateCarouselSlideHandler>.Instance);
            await handler.Handle(new UpdateCarouselSlideCommand(
                slide.Id, "Toggle", "Sub", "Shop Now", 1, false), CancellationToken.None);
        }

        // Fresh cache each read: the invalidation path is covered separately by the unit tests,
        // so this asserts the query itself reflects the new visibility.
        (await Storefront().Handle(new GetStorefrontCarouselQuery(), CancellationToken.None))
            .Value.Should().NotContain(s => s.Title.Contains(tag));
    }

    [SkippableFact]
    public async Task Deleting_a_slide_removes_it_from_the_storefront()
    {
        var tag = Tag();
        var slide = Slide(tag, "doomed", sortOrder: 1, active: true);
        await SeedAsync(tag, slide);

        await using (var ctx = Db.CreateDbContext())
        {
            await new DeleteCarouselSlideHandler(
                    ctx, new NoOpCacheService(), NullLogger<DeleteCarouselSlideHandler>.Instance)
                .Handle(new DeleteCarouselSlideCommand(slide.Id), CancellationToken.None);
        }

        (await Storefront().Handle(new GetStorefrontCarouselQuery(), CancellationToken.None))
            .Value.Should().NotContain(s => s.Title.Contains(tag));

        await using var verify = Db.CreateDbContext();
        (await verify.CarouselSlides.AnyAsync(s => s.Id == slide.Id)).Should().BeFalse();
    }

    [SkippableFact]
    public async Task Creating_a_slide_persists_it_and_returns_the_admin_projection()
    {
        var tag = Tag();

        await using var ctx = Db.CreateDbContext();
        var result = await new CreateCarouselSlideHandler(
                ctx, new NoOpCacheService(), NullLogger<CreateCarouselSlideHandler>.Instance)
            .Handle(new CreateCarouselSlideCommand($"{tag} new", "Sub", "Shop Now", 4, true),
                CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().NotBeEmpty();
        result.Value.Title.Should().Be($"{tag} new");
        result.Value.IsActive.Should().BeTrue();

        await using var verify = Db.CreateDbContext();
        var stored = await verify.CarouselSlides
            .FirstOrDefaultAsync(s => s.Id == result.Value.Id);
        stored.Should().NotBeNull();
        stored!.SortOrder.Should().Be(4);
        // Audit fields are stamped by the DbContext, not by the handler.
        stored.CreatedAtUtc.Should().NotBe(default);
        stored.UpdatedAtUtc.Should().NotBe(default);
    }

    [SkippableFact]
    public async Task Updating_a_missing_slide_is_not_found()
    {
        await using var ctx = Db.CreateDbContext();

        var result = await new UpdateCarouselSlideHandler(
                ctx, new NoOpCacheService(), NullLogger<UpdateCarouselSlideHandler>.Instance)
            .Handle(new UpdateCarouselSlideCommand(
                Guid.NewGuid(), "Ghost", null, null, 0, true), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("CAROUSEL_SLIDE_NOT_FOUND");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Unique marker per test. These tests share one database, so without it one test's slides
    /// would leak into another's assertions.
    /// </summary>
    private static string Tag() => $"ct{Guid.NewGuid():N}"[..10];

    private static string TagOf(string title) => title[..10];

    private static CarouselSlide Slide(
        string tag, string suffix, int sortOrder, bool active,
        string? cta = "Shop Now", bool withImage = true)
    {
        var slide = CarouselSlide.Create($"{tag}-{suffix}", "Sub", cta, sortOrder, active);
        if (withImage) slide.SetImage($"carousel/{tag}", ImageUrl);
        return slide;
    }

    private async Task SeedAsync(string tag, params CarouselSlide[] slides)
    {
        await using var ctx = Db.CreateDbContext();
        ctx.CarouselSlides.AddRange(slides);
        await ctx.SaveChangesAsync();
    }

    private AppDbContext Context() => Db.CreateDbContext();

    /// <summary>A fresh cache per call, so each storefront read queries the database.</summary>
    private GetStorefrontCarouselHandler Storefront()
        => new(
            Db.CreateDbContext(),
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new CatalogCacheOptions { DefaultExpiryMinutes = 10 }));

    /// <summary>
    /// Cache invalidation is an Application-layer concern asserted by the unit tests with a
    /// verifying mock. This integration suite is about what the database returns, so invalidation
    /// is a no-op here rather than a hand-written fake that could disagree with the real service.
    /// </summary>
    private sealed class NoOpCacheService : ICatalogCacheService
    {
        public void InvalidateCategories() { }
        public void InvalidateBrands() { }
        public void InvalidateProducts() { }
        public void InvalidateProduct(Guid productId) { }
        public void InvalidateStorefrontCategories() { }
        public void InvalidateStorefrontBrands() { }
        public void InvalidateStorefrontProduct(string slug) { }
        public void InvalidateStorefrontFeatured() { }
        public void InvalidateCarousel() { }
        public void InvalidatePublicPolicies() { }
        public void InvalidateProductReviews(Guid productId) { }
        public void InvalidateProductGraph() { }
        public void InvalidateProductGraph(Guid productId, string? slug) { }
        public void InvalidateStockGraph(string? slug) { }
        public void InvalidateBrandGraph() { }
public void InvalidateCategoryGraph() { }
        public void InvalidateCatalogStructure() { }
        public void InvalidateShippingConfiguration() { }
        public int GetCatalogEpoch() => 0;
        public int GetShippingEpoch() => 0;
    }
}