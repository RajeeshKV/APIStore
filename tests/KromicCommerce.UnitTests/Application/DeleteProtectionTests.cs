using KromicCommerce.Application.Features.Catalog.Brands.DeleteBrand;
using KromicCommerce.Application.Features.Catalog.Categories.DeleteCategory;
using KromicCommerce.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace KromicCommerce.UnitTests.Application;

/// <summary>
/// Verifies that categories and brands cannot be deleted when they have
/// dependent products or subcategories, returning ErrorType.Conflict as specified.
/// </summary>
public sealed class DeleteProtectionTests
{
    private readonly Mock<IApplicationDbContext> _db = new();
    private readonly Mock<KromicCommerce.Application.Abstractions.Catalog.ICatalogCacheService> _cache = new();

    // -----------------------------------------------------------------------
    // Category delete protection
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DeleteCategory_returns_conflict_when_products_exist()
    {
        var categoryId = Guid.NewGuid();
        var category = BuildCategory(categoryId, "Electronics", "electronics");

        // Single Categories mock that handles both FindAsync and queryable (AnyAsync children check)
        SetupCategoriesWithFind(_db, category, childCategories: []);
        SetupProductsForCategory(_db, categoryId, hasProducts: true);

        var handler = new DeleteCategoryHandler(_db.Object, _cache.Object);
        var result = await handler.Handle(new DeleteCategoryCommand(categoryId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be("CATEGORY_HAS_PRODUCTS");
        _cache.Verify(c => c.InvalidateCategories(), Times.Never);
    }

    [Fact]
    public async Task DeleteCategory_returns_conflict_when_children_exist()
    {
        var categoryId = Guid.NewGuid();
        var category = BuildCategory(categoryId, "Electronics", "electronics");
        var child = BuildCategory(Guid.NewGuid(), "Phones", "phones", categoryId);

        SetupCategoriesWithFind(_db, category, childCategories: [child]);
        SetupProductsForCategory(_db, categoryId, hasProducts: false);

        var handler = new DeleteCategoryHandler(_db.Object, _cache.Object);
        var result = await handler.Handle(new DeleteCategoryCommand(categoryId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be("CATEGORY_HAS_CHILDREN");
        _cache.Verify(c => c.InvalidateCategories(), Times.Never);
    }

    [Fact]
    public async Task DeleteCategory_succeeds_when_empty()
    {
        var categoryId = Guid.NewGuid();
        var category = BuildCategory(categoryId, "Empty", "empty");

        SetupCategoriesWithFind(_db, category, childCategories: []);
        SetupProductsForCategory(_db, categoryId, hasProducts: false);
        _db.Setup(d => d.Categories.Remove(It.IsAny<Category>()));
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new DeleteCategoryHandler(_db.Object, _cache.Object);
        var result = await handler.Handle(new DeleteCategoryCommand(categoryId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _cache.Verify(c => c.InvalidateCategories(), Times.Once);
    }

    [Fact]
    public async Task DeleteCategory_returns_not_found_for_missing_category()
    {
        SetupCategoriesWithFind(_db, null, childCategories: []);

        var handler = new DeleteCategoryHandler(_db.Object, _cache.Object);
        var result = await handler.Handle(new DeleteCategoryCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    // -----------------------------------------------------------------------
    // Brand delete protection
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DeleteBrand_returns_conflict_when_products_exist()
    {
        var brandId = Guid.NewGuid();
        var brand = BuildBrand(brandId, "Nike", "nike");

        SetupBrandsWithFind(_db, brand);
        SetupProductsForBrand(_db, brandId, hasProducts: true);

        var handler = new DeleteBrandHandler(_db.Object, _cache.Object);
        var result = await handler.Handle(new DeleteBrandCommand(brandId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be("BRAND_HAS_PRODUCTS");
        _cache.Verify(c => c.InvalidateBrands(), Times.Never);
    }

    [Fact]
    public async Task DeleteBrand_succeeds_when_no_products()
    {
        var brandId = Guid.NewGuid();
        var brand = BuildBrand(brandId, "Nike", "nike");

        SetupBrandsWithFind(_db, brand);
        SetupProductsForBrand(_db, brandId, hasProducts: false);
        _db.Setup(d => d.Brands.Remove(It.IsAny<Brand>()));
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new DeleteBrandHandler(_db.Object, _cache.Object);
        var result = await handler.Handle(new DeleteBrandCommand(brandId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _cache.Verify(c => c.InvalidateBrands(), Times.Once);
    }

    [Fact]
    public async Task DeleteBrand_returns_not_found_for_missing_brand()
    {
        SetupBrandsWithFind(_db, null);

        var handler = new DeleteBrandHandler(_db.Object, _cache.Object);
        var result = await handler.Handle(new DeleteBrandCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Category BuildCategory(Guid id, string name, string slug, Guid? parentId = null)
    {
        var c = Category.Create(name, slug, null, parentId);
        typeof(KromicCommerce.Domain.Common.Entity).GetProperty("Id")!.SetValue(c, id);
        return c;
    }

    private static Brand BuildBrand(Guid id, string name, string slug)
    {
        var b = Brand.Create(name, slug, null, null);
        typeof(KromicCommerce.Domain.Common.Entity).GetProperty("Id")!.SetValue(b, id);
        return b;
    }

    /// <summary>
    /// Configures a single Categories DbSet mock that handles both:
    /// - FindAsync (for the category being deleted)
    /// - IQueryable/IAsyncEnumerable (for AnyAsync children/products checks)
    /// </summary>
    private static void SetupCategoriesWithFind(
        Mock<IApplicationDbContext> db,
        Category? findResult,
        List<Category> childCategories)
    {
        var data = childCategories.AsQueryable();
        var mock = BuildDbSet(data);
        mock.Setup(m => m.FindAsync(It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(findResult);
        db.Setup(d => d.Categories).Returns(mock.Object);
    }

    private static void SetupBrandsWithFind(Mock<IApplicationDbContext> db, Brand? findResult)
    {
        var data = new List<Brand>().AsQueryable();
        var mock = BuildDbSet(data);
        mock.Setup(m => m.FindAsync(It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(findResult);
        db.Setup(d => d.Brands).Returns(mock.Object);
    }

    private static void SetupProductsForCategory(
        Mock<IApplicationDbContext> db, Guid categoryId, bool hasProducts)
    {
        var products = hasProducts
            ? new List<Product> { CreateProductWithCategoryId(categoryId) }
            : new List<Product>();
        db.Setup(d => d.Products).Returns(BuildDbSet(products.AsQueryable()).Object);
    }

    private static void SetupProductsForBrand(
        Mock<IApplicationDbContext> db, Guid brandId, bool hasProducts)
    {
        var products = hasProducts
            ? new List<Product> { CreateProductWithBrandId(brandId) }
            : new List<Product>();
        db.Setup(d => d.Products).Returns(BuildDbSet(products.AsQueryable()).Object);
    }

    private static Product CreateProductWithCategoryId(Guid categoryId)
        => Product.Create("X", "x", null, 10m, categoryId, null);

    private static Product CreateProductWithBrandId(Guid brandId)
        => Product.Create("X", "x", null, 10m, null, brandId);

    private static Mock<DbSet<T>> BuildDbSet<T>(IQueryable<T> data) where T : class
    {
        var mock = new Mock<DbSet<T>>();
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
