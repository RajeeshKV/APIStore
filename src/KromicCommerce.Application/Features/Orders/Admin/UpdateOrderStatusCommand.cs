namespace KromicCommerce.Application.Features.Orders.Admin;

public sealed record UpdateOrderStatusCommand(
    Guid OrderId,
    string Status,
    string? TrackingNumber,
    string? TrackingProvider,
    string? Reason) : ICommand;
