namespace KromicCommerce.Domain.Identity.Events;

public sealed class CustomerProfileUpdatedEvent(Guid userId) : DomainEvent
{
    public Guid UserId { get; } = userId;
}
