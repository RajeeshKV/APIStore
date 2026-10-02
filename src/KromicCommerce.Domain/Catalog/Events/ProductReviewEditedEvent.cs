namespace KromicCommerce.Domain.Catalog.Events;

public sealed class ProductReviewEditedEvent(
    Guid reviewId,
    Guid customerId,
    Guid productId) : DomainEvent
{
    public Guid ReviewId { get; } = reviewId;
    public Guid CustomerId { get; } = customerId;
    public Guid ProductId { get; } = productId;
}