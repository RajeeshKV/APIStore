namespace KromicCommerce.Domain.Identity.Events;

public sealed class CustomerAddressCreatedEvent(Guid addressId, Guid customerId) : DomainEvent
{
    public Guid AddressId { get; } = addressId;
    public Guid CustomerId { get; } = customerId;
}
