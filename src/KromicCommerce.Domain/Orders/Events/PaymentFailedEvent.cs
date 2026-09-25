namespace KromicCommerce.Domain.Orders.Events;

public sealed class PaymentFailedEvent(Guid paymentId, Guid orderId, string? reason) : DomainEvent
{
    public Guid PaymentId { get; } = paymentId;
    public Guid OrderId { get; } = orderId;
    public string? Reason { get; } = reason;
}
