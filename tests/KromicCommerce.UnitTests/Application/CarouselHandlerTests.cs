using KromicCommerce.Application.Abstractions.Catalog;
using KromicCommerce.Application.Caching;
using KromicCommerce.Application.Features.Admin.Carousel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace KromicCommerce.UnitTests.Application;

/// <summary>
/// Carousel management behaviour: admin CRUD, the public storefront projection, and the rule that
/// a slide CTA can never become an arbitrary link.
/// </summary>
public sealed class CarouselHandlerTests
{
    private readonly Mock<IApplicationDbContext> _db = new();
    private readonly Mock<ICatalogCacheService> _cache = new();
    private readonly MemoryCache _memoryCache = new(new MemoryCacheOptions());
    private readonly List<CarouselSlide> _rows = [];

    private const string Url = "https://res.cloudinary.com/demo/image/upload/carousel/a.jpg";

    // -----------------------------------------------------------------------
    // Admin — create
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Create_persists_the_slide_and_invalidates_the_storefront_cache()
    {
        SetupDb();

        var result = await new CreateCarouselSlideHandler(
                _db.Object, _cache.Object, NullLogger<CreateCarouselSlideHandler>.Instance)
            .Handle(new CreateCarouselSlideCommand("  Summer Sale  ", "  Up to 50% off  ", " Shop Now ", 2, true),
                CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Title.Should().Be("Summer Sale");
        result.Value.Subtitle.Should().Be("Up to 50% off");
        result.Value.CtaText.Should().Be("Shop Now");
        result.Value.SortOrder.Should().Be(2);
        result.Value.IsActive.Should().BeTrue();

        _db.Verify(d => d.CarouselSlides.Add(It.IsAny<CarouselSlide>()), Times.Once);
        _db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        // A published slide must not stay hidden behind a stale cache.
        _cache.Verify(c => c.InvalidateCarousel(), Times.Once);
    }

    [Fact]
    public void Create_rejects_a_blank_title()
    {
        var act = () => CarouselSlide.Create("   ", null, null, 0);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("", "desc", "Shop", 0)]
    [InlineData("   ", "desc", "Shop", 0)]
    [InlineData("Title", "desc", "Shop", -1)]
    public void Create_validator_rejects_invalid_input(string title, string subtitle, string cta, int order)
    {
        var result = new CreateCarouselSlideValidator()
            .Validate(new CreateCarouselSlideCommand(title, subtitle, cta, order, false));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Optional_text_fields_may_be_omitted_or_empty()
    {
        // Subtitle and CTA label are genuinely optional: a slide without a subtitle is normal, and
        // an empty CTA label means the storefront renders the slide with no button. Rejecting
        // empty strings here would force an admin to invent copy they do not want.
        var result = new CreateCarouselSlideValidator()
            .Validate(new CreateCarouselSlideCommand("Sale", null, null, 0, false));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Create_validator_enforces_maximum_lengths()
    {
        var result = new CreateCarouselSlideValidator().Validate(new CreateCarouselSlideCommand(
            new string('t', 201), new string('s', 501), new string('c', 51), 0, false));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(3);
    }

    [Fact]
    public void Create_validator_accepts_a_valid_slide()
    {
        var result = new CreateCarouselSlideValidator()
            .Validate(new CreateCarouselSlideCommand("Sale", "Up to 50% off", "Shop Now", 0, true));

        result.IsValid.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Admin — update / delete
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Update_changes_content_order_and_visibility()
    {
        var slide = ActiveSlide(sortOrder: 5);
        SetupDb([slide]);

        var result = await new UpdateCarouselSlideHandler(
                _db.Object, _cache.Object, NullLogger<UpdateCarouselSlideHandler>.Instance)
            .Handle(new UpdateCarouselSlideCommand(slide.Id, "New Title", "New Sub", "Explore", 1, false),
                CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Title.Should().Be("New Title");
        result.Value.CtaText.Should().Be("Explore");
        result.Value.SortOrder.Should().Be(1);
        // Turning a slide off is a public-visible change, so the cache must drop.
        result.Value.IsActive.Should().BeFalse();
        _cache.Verify(c => c.InvalidateCarousel(), Times.Once);
    }

    [Fact]
    public async Task Update_of_a_missing_slide_is_not_found()
    {
        SetupDb([]);

        var result = await new UpdateCarouselSlideHandler(
                _db.Object, _cache.Object, NullLogger<UpdateCarouselSlideHandler>.Instance)
            .Handle(new UpdateCarouselSlideCommand(Guid.NewGuid(), "T", null, null, 0, true),
                CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("CAROUSEL_SLIDE_NOT_FOUND");
        result.Error.Type.Should().Be(ErrorType.NotFound);
        // Nothing was written, so nothing should have been invalidated.
        _cache.Verify(c => c.InvalidateCarousel(), Times.Never);
    }

    [Fact]
    public async Task Delete_removes_the_slide_and_invalidates_the_cache()
    {
        var slide = ActiveSlide();
        SetupDb([slide]);

        var result = await new DeleteCarouselSlideHandler(
                _db.Object, _cache.Object, NullLogger<DeleteCarouselSlideHandler>.Instance)
            .Handle(new DeleteCarouselSlideCommand(slide.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _db.Verify(d => d.CarouselSlides.Remove(slide), Times.Once);
        _cache.Verify(c => c.InvalidateCarousel(), Times.Once);
    }

    [Fact]
    public async Task Delete_of_a_missing_slide_is_not_found()
    {
        SetupDb([]);

        var result = await new DeleteCarouselSlideHandler(
                _db.Object, _cache.Object, NullLogger<DeleteCarouselSlideHandler>.Instance)
            .Handle(new DeleteCarouselSlideCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("CAROUSEL_SLIDE_NOT_FOUND");
        _cache.Verify(c => c.InvalidateCarousel(), Times.Never);
    }

    [Fact]
    public async Task Get_by_id_returns_the_admin_projection()
    {
        var slide = ActiveSlide();
        SetupDb([slide]);

        var result = await new GetAdminCarouselSlideHandler(_db.Object)
            .Handle(new GetAdminCarouselSlideQuery(slide.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // The admin view carries the Cloudinary public id and audit fields.
        result.Value.ImagePublicId.Should().Be("carousel/a");
        result.Value.ImageUrl.Should().Be(Url);
    }

    [Fact]
    public async Task Get_by_id_of_a_missing_slide_is_not_found()
    {
        SetupDb([]);

        var result = await new GetAdminCarouselSlideHandler(_db.Object)
            .Handle(new GetAdminCarouselSlideQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("CAROUSEL_SLIDE_NOT_FOUND");
    }

    [Fact]
    public async Task Admin_list_can_filter_to_active_slides_only()
    {
        SetupDb([ActiveSlide(), InactiveSlide()]);

        var all = await new GetAdminCarouselSlidesHandler(_db.Object)
            .Handle(new GetAdminCarouselSlidesQuery(false), CancellationToken.None);
        var activeOnly = await new GetAdminCarouselSlidesHandler(_db.Object)
            .Handle(new GetAdminCarouselSlidesQuery(true), CancellationToken.None);

        all.Value.Should().HaveCount(2);
        activeOnly.Value.Should().ContainSingle();
    }

    [Fact]
    public async Task Admin_list_orders_by_display_order()
    {
        SetupDb([Slide("Third", 3), Slide("First", 1), Slide("Second", 2)]);

        var result = await new GetAdminCarouselSlidesHandler(_db.Object)
            .Handle(new GetAdminCarouselSlidesQuery(false), CancellationToken.None);

        result.Value.Select(s => s.Title).Should().ContainInOrder("First", "Second", "Third");
    }

    // -----------------------------------------------------------------------
    // Admin — image
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Uploading_an_image_stores_the_reference_and_refreshes_the_cache()
    {
        var slide = ActiveSlide();
        SetupDb([slide]);

        var result = await new UploadCarouselSlideImageHandler(_db.Object, _cache.Object)
            .Handle(new UploadCarouselSlideImageCommand(slide.Id, "carousel/new", "https://cdn/new.jpg"),
                CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ImagePublicId.Should().Be("carousel/new");
        result.Value.ImageUrl.Should().Be("https://cdn/new.jpg");
        _cache.Verify(c => c.InvalidateCarousel(), Times.Once);
    }

    [Fact]
    public async Task Clearing_the_image_returns_the_slide_to_draft()
    {
        var slide = ActiveSlide();
        SetupDb([slide]);

        var result = await new DeleteCarouselSlideImageHandler(_db.Object, _cache.Object)
            .Handle(new DeleteCarouselSlideImageCommand(slide.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ImageUrl.Should().BeNull();
        result.Value.ImagePublicId.Should().BeNull();
    }

    [Fact]
    public async Task Image_upload_for_a_missing_slide_is_not_found()
    {
        SetupDb([]);

        var result = await new UploadCarouselSlideImageHandler(_db.Object, _cache.Object)
            .Handle(new UploadCarouselSlideImageCommand(Guid.NewGuid(), "p", "u"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("CAROUSEL_SLIDE_NOT_FOUND");
    }

    // -----------------------------------------------------------------------
    // Public storefront
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Storefront_returns_only_active_slides()
    {
        SetupDb([ActiveSlide(), InactiveSlide()]);

        var result = await StorefrontHandler().Handle(
            new GetStorefrontCarouselQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        result.Value[0].Title.Should().Be("Visible");
    }

    [Fact]
    public async Task Storefront_excludes_a_slide_whose_image_never_uploaded()
    {
        // An active slide with no image is a half-finished draft. Returning it would render a
        // hero with a missing image, so the storefront skips it instead.
        var noImage = CarouselSlide.Create("No image", null, null, 0, isActive: true);
        SetupDb([noImage]);

        var result = await StorefrontHandler().Handle(
            new GetStorefrontCarouselQuery(), CancellationToken.None);

        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Storefront_orders_by_display_order_ascending()
    {
        SetupDb([Slide("Third", 3), Slide("First", 1), Slide("Second", 2)]);

        var result = await StorefrontHandler().Handle(
            new GetStorefrontCarouselQuery(), CancellationToken.None);

        result.Value.Select(s => s.Title).Should().ContainInOrder("First", "Second", "Third");
    }

    [Fact]
    public async Task Storefront_ordering_is_deterministic_when_display_orders_tie()
    {
        // SortOrder is admin-supplied and two slides can share it. The query must fall back to
        // CreatedAtUtc and then Id, otherwise the result depends on whatever order the provider
        // happens to return. Which slide wins a tie is not the contract — that the returned
        // sequence matches the full sort tuple is.
        var slides = Enumerable.Range(0, 6)
            .Select(i => Slide($"Tied{i}", sortOrder: 1))
            .ToList();

        SetupDb(slides);

        var result = await StorefrontHandler().Handle(
            new GetStorefrontCarouselQuery(), CancellationToken.None);

        var expected = slides
            .OrderBy(s => s.SortOrder).ThenBy(s => s.CreatedAtUtc).ThenBy(s => s.Id)
            .Select(s => s.Title)
            .ToList();

        result.Value.Select(s => s.Title).Should().Equal(expected);
        // Repeated reads must agree; a missing tie-breaker can reshuffle tied slides.
        var again = await StorefrontHandler().Handle(
            new GetStorefrontCarouselQuery(), CancellationToken.None);
        again.Value.Select(s => s.Title).Should().Equal(expected);
    }

    [Fact]
    public async Task Storefront_never_exposes_admin_only_fields()
    {
        // The Cloudinary public id and audit metadata are admin concerns. The response type does
        // not contain them at all, which is the strongest form of the guarantee.
        SetupDb([ActiveSlide()]);

        var result = await StorefrontHandler().Handle(
            new GetStorefrontCarouselQuery(), CancellationToken.None);

        var slide = result.Value.Single();
        typeof(StorefrontCarouselSlideResponse)
            .GetProperties().Select(p => p.Name)
            .Should().NotContain("ImagePublicId")
            .And.NotContain("IsActive")
            .And.NotContain("CreatedAtUtc")
            .And.NotContain("UpdatedAtUtc");
        slide.ImageUrl.Should().Be(Url);
    }

    [Fact]
    public async Task Every_storefront_cta_points_at_the_fixed_shop_route()
    {
        // The CTA destination is a product requirement, not configuration: there is no URL field
        // anywhere in the contract, so a slide cannot become an arbitrary outbound link.
        SetupDb([ActiveSlide()]);

        var result = await StorefrontHandler().Handle(
            new GetStorefrontCarouselQuery(), CancellationToken.None);

        result.Value.Single().CtaTarget.Should().Be("/shop");
        StorefrontCarouselSlideResponse.FixedCtaTarget.Should().Be("/shop");
    }

    [Fact]
    public async Task Storefront_returns_an_empty_list_when_nothing_is_configured()
    {
        // An empty carousel is a valid state, not an error.
        SetupDb([]);

        var result = await StorefrontHandler().Handle(
            new GetStorefrontCarouselQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private GetStorefrontCarouselHandler StorefrontHandler()
    {
        // A real MemoryCache, so the handler's cache-hit and cache-write paths both behave. The
        // key is evicted before each read to make every test exercise the query.
        _memoryCache.Remove(CatalogCacheKeys.StorefrontCarousel);
        var cacheOptions = Options.Create(new CatalogCacheOptions { DefaultExpiryMinutes = 10 });

        return new GetStorefrontCarouselHandler(_db.Object, _memoryCache, cacheOptions);
    }

    private void SetupDb(List<CarouselSlide>? rows = null)
    {
        _rows.Clear();
        if (rows is not null) _rows.AddRange(rows);

        var mock = new Mock<DbSet<CarouselSlide>>();
        var queryable = _rows.AsQueryable();
        mock.Setup(d => d.Add(It.IsAny<CarouselSlide>())).Callback<CarouselSlide>(_rows.Add);
        mock.Setup(d => d.Remove(It.IsAny<CarouselSlide>()))
            .Callback<CarouselSlide>(s => _rows.RemoveAll(r => r.Id == s.Id));
        mock.As<IQueryable<CarouselSlide>>().Setup(m => m.Provider)
            .Returns(new TestAsyncQueryProvider<CarouselSlide>(queryable.Provider));
        mock.As<IQueryable<CarouselSlide>>().Setup(m => m.Expression).Returns(queryable.Expression);
        mock.As<IQueryable<CarouselSlide>>().Setup(m => m.ElementType).Returns(queryable.ElementType);
        mock.As<IQueryable<CarouselSlide>>().Setup(m => m.GetEnumerator()).Returns(queryable.GetEnumerator());
        mock.As<IAsyncEnumerable<CarouselSlide>>()
            .Setup(m => m.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns(new TestAsyncEnumerator<CarouselSlide>(queryable.GetEnumerator()));

        _db.Setup(d => d.CarouselSlides).Returns(mock.Object);
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private static CarouselSlide ActiveSlide(int sortOrder = 0)
    {
        var s = CarouselSlide.Create("Visible", "Sub", "Shop Now", sortOrder, isActive: true);
        s.SetImage("carousel/a", Url);
        return s;
    }

    private static CarouselSlide InactiveSlide()
    {
        var s = CarouselSlide.Create("Hidden", null, "Shop Now", 1, isActive: false);
        s.SetImage("carousel/b", Url);
        return s;
    }

    private static CarouselSlide Slide(string title, int sortOrder)
    {
        var s = CarouselSlide.Create(title, null, "Shop Now", sortOrder, isActive: true);
        s.SetImage($"carousel/{title}", Url);
        return s;
    }
}