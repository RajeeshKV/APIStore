namespace KromicCommerce.Domain.Identity.Events;

public sealed class WishlistItemRemovedEvent(
    Guid wishlistItemId,
    Guid customerId,
    Guid productId,
    Guid? productVariantId) : DomainEvent
{
    public Guid WishlistItemId { get; } = wishlistItemId;
    public Guid CustomerId { get; } = customerId;
    public Guid ProductId { get; } = productId;
    public Guid? ProductVariantId { get; } = productVariantId;
}