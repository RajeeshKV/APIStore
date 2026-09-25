using KromicCommerce.Application.Abstractions.Catalog;
using KromicCommerce.Domain.Catalog;

namespace KromicCommerce.Application.Services;

/// <summary>
/// Translates a domain InventoryItem into the public StockAvailability/CanPurchase pair.
/// Registered as a singleton — pure logic, no side effects.
/// </summary>
public sealed class StorefrontStockService : IStorefrontStockService
{
    public PublicStockResponse GetStockResponse(InventoryItem? inventory)
    {
        // No inventory record → product is not tracked; assume purchasable
        if (inventory is null)
            return new PublicStockResponse(StockAvailability.InStock, CanPurchase: true);

        if (inventory.IsOutOfStock)
            return new PublicStockResponse(StockAvailability.OutOfStock, CanPurchase: false);

        if (inventory.IsLowStock)
            return new PublicStockResponse(StockAvailability.LowStock, CanPurchase: true);

        return new PublicStockResponse(StockAvailability.InStock, CanPurchase: true);
    }
}
