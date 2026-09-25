namespace KromicCommerce.Application.Abstractions.Catalog;

/// <summary>
/// Centralises the translation from domain inventory state to a public StockAvailability.
/// All storefront handlers must use this service — never derive stock status ad-hoc.
///
/// Rules:
///   inventory is null (no record)   → InStock / CanPurchase = true  (no stock tracking)
///   Available > LowStockThreshold   → InStock
///   Available > 0                   → LowStock
///   Available &lt;= 0                 → OutOfStock / CanPurchase = false
/// </summary>
public interface IStorefrontStockService
{
    PublicStockResponse GetStockResponse(KromicCommerce.Domain.Catalog.InventoryItem? inventory);
}
