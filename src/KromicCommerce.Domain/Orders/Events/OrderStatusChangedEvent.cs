namespace KromicCommerce.Domain.Orders.Events;

public sealed class OrderStatusChangedEvent(Guid orderId, OrderStatus newStatus) : DomainEvent
{
    public Guid OrderId { get; } = orderId;
    public OrderStatus NewStatus { get; } = newStatus;
}
