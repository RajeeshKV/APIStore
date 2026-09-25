namespace KromicCommerce.Domain.Identity.Events;

public sealed class DefaultAddressChangedEvent(Guid customerId, Guid newDefaultAddressId) : DomainEvent
{
    public Guid CustomerId { get; } = customerId;
    public Guid NewDefaultAddressId { get; } = newDefaultAddressId;
}
