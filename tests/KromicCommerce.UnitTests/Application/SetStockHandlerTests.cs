using KromicCommerce.Application.Features.Catalog.Inventory;
using KromicCommerce.Domain.Catalog;
using Microsoft.Extensions.Logging.Abstractions;

namespace KromicCommerce.UnitTests.Application;

public sealed class SetStockHandlerTests
{
    private readonly Mock<IApplicationDbContext> _db = new();
    private readonly Mock<ICatalogCacheService> _cache = new();

    private SetStockHandler CreateHandler() =>
        new(_db.Object, _cache.Object, NullLogger<SetStockHandler>.Instance);

    [Fact]
    public async Task Creates_new_inventory_record_when_none_exists()
    {
        var productId = Guid.NewGuid();
        SetupNoInventory();
        SetupProductExists(productId); // ensures AnyAsync returns true
        _db.Setup(d => d.InventoryItems.Add(It.IsAny<InventoryItem>()));
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateHandler().Handle(
            new SetStockCommand(productId, null, 50, 5),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.OnHand.Should().Be(50);
        result.Value.LowStockThreshold.Should().Be(5);
    }

    [Fact]
    public async Task Returns_not_found_when_product_does_not_exist()
    {
        SetupNoInventory();
        SetupProductNotExists();

        var result = await CreateHandler().Handle(
            new SetStockCommand(Guid.NewGuid(), null, 10, 5),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("PRODUCT_NOT_FOUND");
    }

    [Theory]
    [InlineData(-1, 5)]   // negative on-hand
    [InlineData(10, -1)]  // negative threshold
    public void Validator_rejects_invalid_stock_values(int onHand, int threshold)
    {
        var validator = new SetStockValidator();
        var result = validator.Validate(new SetStockCommand(Guid.NewGuid(), null, onHand, threshold));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_accepts_zero_on_hand()
    {
        var validator = new SetStockValidator();
        var result = validator.Validate(new SetStockCommand(Guid.NewGuid(), null, 0, 5));
        result.IsValid.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // AdjustStock validator
    // -----------------------------------------------------------------------

    [Fact]
    public void AdjustStockValidator_rejects_zero_delta()
    {
        var validator = new AdjustStockValidator();
        validator.Validate(new AdjustStockCommand(Guid.NewGuid(), null, 0, null)).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(5)]
    [InlineData(-3)]
    public void AdjustStockValidator_accepts_nonzero_delta(int delta)
    {
        var validator = new AdjustStockValidator();
        validator.Validate(new AdjustStockCommand(Guid.NewGuid(), null, delta, null)).IsValid.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void SetupNoInventory()
    {
        var data = new List<InventoryItem>().AsQueryable();
        var mock = BuildMock(data);
        _db.Setup(d => d.InventoryItems).Returns(mock.Object);
    }

    private void SetupProductExists(Guid productId)
    {
        // Build a queryable containing one product whose Id matches productId.
        // We use reflection to set the inherited Entity.Id since Create() assigns a new Guid.
        var product = Product.Create("X", "x", null, 10m, null, null);
        typeof(KromicCommerce.Domain.Common.Entity)
            .GetProperty("Id")!
            .SetValue(product, productId);

        var data = new List<Product> { product }.AsQueryable();
        var mock = BuildMock(data);
        _db.Setup(d => d.Products).Returns(mock.Object);
    }

    private void SetupProductNotExists()
    {
        var data = new List<Product>().AsQueryable();
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
