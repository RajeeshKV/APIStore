namespace KromicCommerce.Contracts.Orders;

public sealed record OrderSummaryResponse(
    Guid Id,
    string OrderNumber,
    string Status,
    string PaymentMethod,
    decimal GrandTotal,
    string Currency,
    int ItemCount,
    DateTime CreatedAtUtc);
