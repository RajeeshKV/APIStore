namespace KromicCommerce.Contracts.Orders;

public sealed record UpdateOrderStatusRequest(
    string Status,
    string? TrackingNumber = null,
    string? TrackingProvider = null,
    string? Reason = null);
