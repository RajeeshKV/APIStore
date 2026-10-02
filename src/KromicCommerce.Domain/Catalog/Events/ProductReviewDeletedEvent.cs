namespace KromicCommerce.Domain.Catalog.Events;

public sealed class ProductReviewDeletedEvent(
    Guid reviewId,
    Guid customerId,
    Guid productId,
    Guid? productVariantId) : DomainEvent
{
    public Guid ReviewId { get; } = reviewId;
    public Guid CustomerId { get; } = customerId;
    public Guid ProductId { get; } = productId;
    public Guid? ProductVariantId { get; } = productVariantId;
}