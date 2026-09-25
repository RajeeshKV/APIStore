using KromicCommerce.Application.Features.Catalog.Products.ChangeProductStatus;
using KromicCommerce.Application.Features.Catalog.Products.UpdateProduct;
using KromicCommerce.Domain.Catalog;
using Microsoft.Extensions.Logging.Abstractions;

namespace KromicCommerce.UnitTests.Application;

/// <summary>
/// Verifies that storefront cache is invalidated after every product mutation.
/// </summary>
public sealed class StorefrontCacheInvalidationTests
{
    private readonly Mock<IApplicationDbContext> _db = new();
    private readonly Mock<ICatalogCacheService> _cache = new();

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Product BuildProduct(string slug)
    {
        var p = Product.Create("Test", slug, null, 10m, null, null);
        typeof(KromicCommerce.Domain.Common.Entity)
            .GetProperty("Id")!
            .SetValue(p, Guid.NewGuid());
        return p;
    }

    private void SetupDbProduct(Product product)
    {
        _db.Setup(d => d.Products.FindAsync(It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    // -----------------------------------------------------------------------
    // Publish
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Publish_invalidates_storefront_product_cache()
    {
        var product = BuildProduct("my-product");
        SetupDbProduct(product);

        var handler = new PublishProductHandler(_db.Object, _cache.Object);
        var result = await handler.Handle(
            new PublishProductCommand(product.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _cache.Verify(c => c.InvalidateStorefrontProduct("my-product"), Times.Once);
        _cache.Verify(c => c.InvalidateStorefrontFeatured(), Times.Once);
    }

    // -----------------------------------------------------------------------
    // Archive
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Archive_invalidates_storefront_product_cache()
    {
        var product = BuildProduct("archived-product");
        SetupDbProduct(product);
        product.Publish(); // move to Active first so Archive is valid

        var handler = new ArchiveProductHandler(_db.Object, _cache.Object);
        var result = await handler.Handle(
            new ArchiveProductCommand(product.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _cache.Verify(c => c.InvalidateStorefrontProduct("archived-product"), Times.Once);
    }

    // -----------------------------------------------------------------------
    // UpdateProduct
    // -----------------------------------------------------------------------

    [Fact]
    public async Task UpdateProduct_invalidates_storefront_product_cache()
    {
        var product = BuildProduct("update-me");
        SetupDbProduct(product);

        // Need Products mock for slug/sku conflict checks
        var data = new List<Product>().AsQueryable();
        var mockSet = new Mock<Microsoft.EntityFrameworkCore.DbSet<Product>>();
        mockSet.As<IAsyncEnumerable<Product>>()
            .Setup(m => m.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns(new TestAsyncEnumerator<Product>(data.GetEnumerator()));
        mockSet.As<IQueryable<Product>>()
            .Setup(m => m.Provider)
            .Returns(new TestAsyncQueryProvider<Product>(data.Provider));
        mockSet.As<IQueryable<Product>>().Setup(m => m.Expression).Returns(data.Expression);
        mockSet.As<IQueryable<Product>>().Setup(m => m.ElementType).Returns(data.ElementType);
        mockSet.As<IQueryable<Product>>().Setup(m => m.GetEnumerator()).Returns(data.GetEnumerator());
        _db.Setup(d => d.Products).Returns(mockSet.Object);
        _db.Setup(d => d.Products.FindAsync(It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        var handler = new UpdateProductHandler(_db.Object, _cache.Object);
        var result = await handler.Handle(
            new UpdateProductCommand(
                product.Id, "New Name", "update-me", null,
                10m, null, null, null, null, null, false, true, null, null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _cache.Verify(c => c.InvalidateStorefrontProduct("update-me"), Times.Once);
    }
}
