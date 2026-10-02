namespace KromicCommerce.Domain.Catalog.Events;

public sealed class ProductReviewSubmittedEvent(
    Guid reviewId,
    Guid customerId,
    Guid productId,
    ReviewStatus initialStatus) : DomainEvent
{
    public Guid ReviewId { get; } = reviewId;
    public Guid CustomerId { get; } = customerId;
    public Guid ProductId { get; } = productId;
    public ReviewStatus InitialStatus { get; } = initialStatus;
}