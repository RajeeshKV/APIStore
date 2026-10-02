namespace KromicCommerce.Domain.Catalog.Events;

public sealed class ProductReviewStatusChangedEvent(
    Guid reviewId,
    Guid productId,
    ReviewStatus previousStatus,
    ReviewStatus newStatus) : DomainEvent
{
    public Guid ReviewId { get; } = reviewId;
    public Guid ProductId { get; } = productId;
    public ReviewStatus PreviousStatus { get; } = previousStatus;
    public ReviewStatus NewStatus { get; } = newStatus;
}