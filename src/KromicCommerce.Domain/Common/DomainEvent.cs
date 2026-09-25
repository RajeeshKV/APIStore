namespace KromicCommerce.Domain.Common;

/// <summary>
/// Base class for all domain events.
/// Domain events are raised within the aggregate and dispatched
/// after the transaction commits via the Infrastructure/Application layer.
/// </summary>
public abstract class DomainEvent
{
    protected DomainEvent()
    {
        OccurredAtUtc = DateTime.UtcNow;
        EventId = Guid.NewGuid();
    }

    public Guid EventId { get; }
    public DateTime OccurredAtUtc { get; }
}
