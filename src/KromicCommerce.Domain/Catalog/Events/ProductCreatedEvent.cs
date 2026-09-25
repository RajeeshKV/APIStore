namespace KromicCommerce.Domain.Catalog.Events;

public sealed class ProductCreatedEvent(Guid productId, string slug) : DomainEvent
{
    public Guid ProductId { get; } = productId;
    public string Slug { get; } = slug;
}
