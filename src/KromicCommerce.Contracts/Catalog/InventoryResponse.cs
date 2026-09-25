namespace KromicCommerce.Contracts.Catalog;

public sealed record InventoryResponse(
    Guid Id,
    Guid ProductId,
    Guid? VariantId,
    int OnHand,
    int Reserved,
    int Available,
    int LowStockThreshold,
    bool IsLowStock,
    bool IsOutOfStock,
    DateTime UpdatedAt);
