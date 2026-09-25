using KromicCommerce.Application.Abstractions.Catalog;
using KromicCommerce.Application.Features.Catalog.Products.CreateProduct;
using KromicCommerce.Domain.Catalog;
using Microsoft.Extensions.Logging.Abstractions;

namespace KromicCommerce.UnitTests.Application;

public sealed class CreateProductHandlerTests
{
    private readonly Mock<IApplicationDbContext> _db = new();
    private readonly Mock<ICatalogCacheService> _cache = new();

    private CreateProductHandler CreateHandler() =>
        new(_db.Object, _cache.Object, NullLogger<CreateProductHandler>.Instance);

    private static CreateProductCommand ValidCmd(string slug = "red-widget") =>
        new("Red Widget", slug, null, 99.99m, null, null, null, null, null, false, true, null, null, null);

    [Fact]
    public async Task Creates_product_in_draft_status_and_invalidates_cache()
    {
        SetupNoProducts();
        _db.Setup(d => d.Products.Add(It.IsAny<Product>()));
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateHandler().Handle(ValidCmd(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(ProductStatus.Draft.ToString());
        result.Value.Price.Should().Be(99.99m);
        _cache.Verify(c => c.InvalidateProducts(), Times.Once);
    }

    [Fact]
    public async Task Returns_conflict_on_duplicate_slug()
    {
        SetupExistingProduct("red-widget");
        var result = await CreateHandler().Handle(ValidCmd("red-widget"), CancellationToken.None);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("PRODUCT_SLUG_TAKEN");
    }

    [Theory]
    [InlineData("", "valid-slug", 10)]      // empty name
    [InlineData("Valid", "", 10)]            // empty slug
    [InlineData("Valid", "valid-slug", -5)]  // negative price
    public void Validator_rejects_invalid_product(string name, string slug, decimal price)
    {
        var validator = new CreateProductValidator();
        var cmd = new CreateProductCommand(name, slug, null, price, null, null, null, null, null, false, true, null, null, null);
        validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_accepts_zero_price()
    {
        var validator = new CreateProductValidator();
        var cmd = new CreateProductCommand("Free Item", "free-item", null, 0m, null, null, null, null, null, false, true, null, null, null);
        validator.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validator_rejects_compareAtPrice_less_than_price()
    {
        var validator = new CreateProductValidator();
        var cmd = ValidCmd() with { Price = 100m, CompareAtPrice = 80m };
        validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_accepts_valid_product()
    {
        var validator = new CreateProductValidator();
        validator.Validate(ValidCmd()).IsValid.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void SetupNoProducts()
    {
        var data = new List<Product>().AsQueryable();
        var mock = BuildMock(data);
        _db.Setup(d => d.Products).Returns(mock.Object);
    }

    private void SetupExistingProduct(string slug)
    {
        var existing = Product.Create("Old", slug, null, 10m, null, null);
        var data = new List<Product> { existing }.AsQueryable();
        var mock = BuildMock(data);
        _db.Setup(d => d.Products).Returns(mock.Object);
    }

    private static Mock<Microsoft.EntityFrameworkCore.DbSet<T>> BuildMock<T>(IQueryable<T> data)
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
