namespace KromicCommerce.Domain.Catalog.Events;

public sealed class CategoryCreatedEvent(Guid categoryId, string slug) : DomainEvent
{
    public Guid CategoryId { get; } = categoryId;
    public string Slug { get; } = slug;
}
