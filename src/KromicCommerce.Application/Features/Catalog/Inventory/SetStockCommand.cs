namespace KromicCommerce.Application.Features.Catalog.Inventory;

public sealed record SetStockCommand(
    Guid ProductId,
    Guid? VariantId,
    int OnHand,
    int LowStockThreshold) : ICommand<InventoryResponse>;

public sealed record AdjustStockCommand(
    Guid ProductId,
    Guid? VariantId,
    int Delta,
    string? Reason) : ICommand<InventoryResponse>;
