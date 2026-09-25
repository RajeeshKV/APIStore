namespace KromicCommerce.Domain.Catalog.Events;

public sealed class ProductStatusChangedEvent(Guid productId, ProductStatus newStatus) : DomainEvent
{
    public Guid ProductId { get; } = productId;
    public ProductStatus NewStatus { get; } = newStatus;
}
