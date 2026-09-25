using KromicCommerce.Application.Services;
using KromicCommerce.Domain.Catalog;

namespace KromicCommerce.UnitTests.Application;

public sealed class StorefrontStockServiceTests
{
    private readonly StorefrontStockService _svc = new();

    [Fact]
    public void Null_inventory_returns_InStock_and_CanPurchase()
    {
        var result = _svc.GetStockResponse(null);
        result.Availability.Should().Be(StockAvailability.InStock);
        result.CanPurchase.Should().BeTrue();
    }

    [Fact]
    public void OutOfStock_inventory_returns_OutOfStock_CanPurchase_false()
    {
        var inv = InventoryItem.Create(Guid.NewGuid(), null, 0);
        var result = _svc.GetStockResponse(inv);
        result.Availability.Should().Be(StockAvailability.OutOfStock);
        result.CanPurchase.Should().BeFalse();
    }

    [Fact]
    public void LowStock_inventory_returns_LowStock_CanPurchase_true()
    {
        var inv = InventoryItem.Create(Guid.NewGuid(), null, 5, lowStockThreshold: 5);
        // Available == threshold → IsLowStock
        var result = _svc.GetStockResponse(inv);
        result.Availability.Should().Be(StockAvailability.LowStock);
        result.CanPurchase.Should().BeTrue();
    }

    [Fact]
    public void Above_threshold_inventory_returns_InStock()
    {
        var inv = InventoryItem.Create(Guid.NewGuid(), null, 50, lowStockThreshold: 5);
        var result = _svc.GetStockResponse(inv);
        result.Availability.Should().Be(StockAvailability.InStock);
        result.CanPurchase.Should().BeTrue();
    }
}
