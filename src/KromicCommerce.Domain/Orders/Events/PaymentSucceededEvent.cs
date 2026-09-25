namespace KromicCommerce.Domain.Orders.Events;

public sealed class PaymentSucceededEvent(Guid paymentId, Guid orderId, decimal amount, string currencyCode)
    : DomainEvent
{
    public Guid PaymentId { get; } = paymentId;
    public Guid OrderId { get; } = orderId;
    public decimal Amount { get; } = amount;
    public string CurrencyCode { get; } = currencyCode;
}
