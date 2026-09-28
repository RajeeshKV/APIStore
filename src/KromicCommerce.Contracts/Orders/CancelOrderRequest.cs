namespace KromicCommerce.Contracts.Orders;

/// <summary>
/// Optional body for customer order cancellation.
/// Reason is shown to the customer in their order history.
/// </summary>
public sealed record CancelOrderRequest(string? Reason = null);
