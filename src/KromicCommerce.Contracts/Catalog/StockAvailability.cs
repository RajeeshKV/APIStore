namespace KromicCommerce.Contracts.Catalog;

/// <summary>
/// Public-safe stock status enum exposed to storefront customers.
/// Never exposes raw OnHand or Reserved counts to the public API.
/// </summary>
public enum StockAvailability
{
    InStock,
    LowStock,
    OutOfStock
}

/// <summary>
/// Stock information suitable for public (customer-facing) API responses.
/// Internal counts (OnHand, Reserved) are intentionally omitted.
/// </summary>
public sealed record PublicStockResponse(
    StockAvailability Availability,

    /// <summary>
    /// True when the product/variant can be added to cart.
    /// Always derived server-side — never trust client-supplied values.
    /// </summary>
    bool CanPurchase);
