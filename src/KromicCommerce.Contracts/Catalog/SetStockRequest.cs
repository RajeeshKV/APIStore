namespace KromicCommerce.Contracts.Catalog;

public sealed record SetStockRequest(
    int OnHand,
    int LowStockThreshold = 5);
