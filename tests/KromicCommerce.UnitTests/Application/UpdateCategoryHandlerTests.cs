using KromicCommerce.Application.Abstractions.Catalog;
using KromicCommerce.Application.Features.Catalog.Brands.UpdateBrand;
using KromicCommerce.Application.Features.Catalog.Categories.UpdateCategory;
using KromicCommerce.Domain.Catalog;

namespace KromicCommerce.UnitTests.Application;

public sealed class UpdateCategoryHandlerTests
{
    [Fact]
    public async Task Preserves_activation_state_when_is_active_is_omitted()
    {
        var category = Category.Create("Electronics", "electronics", null, null);
        var categories = BuildMockDbSet(new List<Category> { category }.AsQueryable());
        categories.Setup(s => s.FindAsync(It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);

        var db = new Mock<IApplicationDbContext>();
        db.SetupGet(d => d.Categories).Returns(categories.Object);
        db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var cache = new Mock<ICatalogCacheService>();
        var handler = new UpdateCategoryHandler(db.Object, cache.Object);

        var result = await handler.Handle(
            new UpdateCategoryCommand(category.Id, "Electronics", "electronics", null, null, 0, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Brand_update_preserves_activation_state_when_is_active_is_omitted()
    {
        var brand = Brand.Create("Kromic", "kromic", null, null);
        var brands = BuildMockDbSet(new List<Brand> { brand }.AsQueryable());
        brands.Setup(s => s.FindAsync(It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(brand);

        var db = new Mock<IApplicationDbContext>();
        db.SetupGet(d => d.Brands).Returns(brands.Object);
        db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var cache = new Mock<ICatalogCacheService>();
        var handler = new UpdateBrandHandler(db.Object, cache.Object);

        var result = await handler.Handle(
            new UpdateBrandCommand(brand.Id, "Kromic", "kromic", null, null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsActive.Should().BeTrue();
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
