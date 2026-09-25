namespace KromicCommerce.Domain.Promotions;

/// <summary>
/// Records a single confirmed use of a promotion by a customer.
/// Written inside the checkout transaction only after payment succeeds (or COD confirm).
///
/// One row per successful order that applied a promotion.
/// Used to enforce per-customer usage limits without relying on in-memory state.
///
/// The combination (PromotionId, CustomerId, OrderId) is unique.
/// </summary>
public sealed class PromotionUsage
{
    private PromotionUsage() { } // EF constructor

    public static PromotionUsage Create(Guid promotionId, Guid customerId, Guid orderId)
        => new()
        {
            Id = Guid.NewGuid(),
            PromotionId = promotionId,
            CustomerId = customerId,
            OrderId = orderId,
            UsedAt = DateTime.UtcNow
        };

    public Guid Id { get; private set; }
    public Guid PromotionId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid OrderId { get; private set; }
    public DateTime UsedAt { get; private set; }

    // Navigation
    public Promotion Promotion { get; private set; } = null!;
}
