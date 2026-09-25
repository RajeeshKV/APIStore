namespace KromicCommerce.Contracts.Catalog;

public sealed record AdjustStockRequest(
    int Delta,
    string? Reason = null);
