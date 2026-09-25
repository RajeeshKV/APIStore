namespace KromicCommerce.Contracts.Orders;

public sealed record OrderItemResponse(
    Guid Id,
    Guid ProductId,
    Guid? VariantId,
    string ProductName,
    string? VariantDescription,
    string? Sku,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal);
