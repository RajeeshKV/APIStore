using KromicCommerce.Application.Abstractions.Catalog;
using KromicCommerce.Application.Features.Catalog.Categories.CreateCategory;
using KromicCommerce.Domain.Catalog;
using Microsoft.Extensions.Logging.Abstractions;

namespace KromicCommerce.UnitTests.Application;

public sealed class CreateCategoryHandlerTests
{
    private readonly Mock<IApplicationDbContext> _db = new();
    private readonly Mock<ICatalogCacheService> _cache = new();

    private CreateCategoryHandler CreateHandler() =>
        new(_db.Object, _cache.Object, NullLogger<CreateCategoryHandler>.Instance);

    [Fact]
    public async Task Creates_category_and_invalidates_cache()
    {
        SetupNoDuplicateSlug();
        _db.Setup(d => d.Categories.Add(It.IsAny<Category>()));
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateHandler().Handle(
            new CreateCategoryCommand("Electronics", "electronics", null, null, 0),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Electronics");
        result.Value.Slug.Should().Be("electronics");
        _cache.Verify(c => c.InvalidateCategories(), Times.Once);
    }

    [Fact]
    public async Task Returns_conflict_when_slug_already_exists()
    {
        SetupDuplicateSlug();

        var result = await CreateHandler().Handle(
            new CreateCategoryCommand("Dup", "electronics", null, null, 0),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("CATEGORY_SLUG_TAKEN");
        _cache.Verify(c => c.InvalidateCategories(), Times.Never);
    }

    [Theory]
    [InlineData("", "slug")]
    [InlineData("Name", "")]
    [InlineData("Name", "UPPERCASE-SLUG")]  // slug must be lowercase
    public void Validator_rejects_invalid_input(string name, string slug)
    {
        var validator = new CreateCategoryValidator();
        var result = validator.Validate(new CreateCategoryCommand(name, slug, null, null, 0));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_accepts_valid_slug()
    {
        var validator = new CreateCategoryValidator();
        var result = validator.Validate(new CreateCategoryCommand("Electronics", "electronics-2024", null, null, 0));
        result.IsValid.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void SetupNoDuplicateSlug()
    {
        var data = new List<Category>().AsQueryable();
        var mock = BuildMockDbSet(data);
        _db.Setup(d => d.Categories).Returns(mock.Object);
    }

    private void SetupDuplicateSlug()
    {
        var existing = Category.Create("Electronics", "electronics", null, null);
        var data = new List<Category> { existing }.AsQueryable();
        var mock = BuildMockDbSet(data);
        _db.Setup(d => d.Categories).Returns(mock.Object);
    }

    private static Mock<Microsoft.EntityFrameworkCore.DbSet<T>> BuildMockDbSet<T>(IQueryable<T> data)
        where T : class
    {
        var mock = new Mock<Microsoft.EntityFrameworkCore.DbSet<T>>();
        mock.As<IAsyncEnumerable<T>>()
            .Setup(m => m.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns(new TestAsyncEnumerator<T>(data.GetEnumerator()));
        mock.As<IQueryable<T>>().Setup(m => m.Provider)
            .Returns(new TestAsyncQueryProvider<T>(data.Provider));
        mock.As<IQueryable<T>>().Setup(m => m.Expression).Returns(data.Expression);
        mock.As<IQueryable<T>>().Setup(m => m.ElementType).Returns(data.ElementType);
        mock.As<IQueryable<T>>().Setup(m => m.GetEnumerator()).Returns(data.GetEnumerator());
        return mock;
    }
}
