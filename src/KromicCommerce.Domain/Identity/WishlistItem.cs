using KromicCommerce.Domain.Identity.Events;

namespace KromicCommerce.Domain.Identity;

/// <summary>
/// A product (optionally a specific variant) saved to a customer's wishlist.
///
/// Mirrors <see cref="CustomerAddress"/> deliberately: an AuditableEntity with a scalar
/// CustomerId and no navigation to User, because ownership is always resolved from
/// ICurrentUserService and never from the request.
///
/// Uniqueness of (CustomerId, ProductId, ProductVariantId) is enforced by a database unique
/// index created with NULLS NOT DISTINCT. A plain unique index would treat NULL variant as
/// distinct and permit unlimited product-level rows for one customer, so the index — not a
/// handler pre-check — is the real guard. A handler check alone loses to concurrent adds.
///
/// The variant, once set, is immutable. Changing it is expressed as remove-then-add, which
/// keeps the unique index trivial and keeps the customer's history honest.
///
/// Hard delete, consistent with the rest of this schema — there is no soft delete anywhere.
/// </summary>
public sealed class WishlistItem : AuditableEntity
{
    private WishlistItem() { } // EF constructor

    public static WishlistItem Create(Guid customerId, Guid productId, Guid? productVariantId = null)
    {
        if (customerId == Guid.Empty)
            throw new ArgumentException("Customer id is required.", nameof(customerId));
        if (productId == Guid.Empty)
            throw new ArgumentException("Product id is required.", nameof(productId));

        var item = new WishlistItem
        {
            CustomerId = customerId,
            ProductId = productId,
            ProductVariantId = productVariantId == Guid.Empty ? null : productVariantId
        };
        item.RaiseDomainEvent(new WishlistItemAddedEvent(item.Id, customerId, productId, productVariantId));
        return item;
    }

    public Guid CustomerId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid? ProductVariantId { get; private set; }

    /// <summary>
    /// Raises the removal event. Called by the handler that deletes the row, immediately
    /// before it is removed — a deleted row cannot raise events from inside its own constructor.
    /// </summary>
    public void RaiseRemoved() =>
        RaiseDomainEvent(new WishlistItemRemovedEvent(Id, CustomerId, ProductId, ProductVariantId));
}