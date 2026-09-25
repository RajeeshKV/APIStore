namespace KromicCommerce.Domain.Orders.Events;

public sealed class OrderCreatedEvent(Guid orderId, Guid customerId, decimal grandTotal, string currencyCode)
    : DomainEvent
{
    public Guid OrderId { get; } = orderId;
    public Guid CustomerId { get; } = customerId;
    public decimal GrandTotal { get; } = grandTotal;
    public string CurrencyCode { get; } = currencyCode;
}
