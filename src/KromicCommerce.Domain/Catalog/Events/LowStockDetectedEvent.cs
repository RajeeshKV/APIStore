namespace KromicCommerce.Domain.Catalog.Events;

public sealed class LowStockDetectedEvent(
    Guid productId,
    Guid? variantId,
    int available,
    int threshold) : DomainEvent
{
    public Guid ProductId { get; } = productId;
    public Guid? VariantId { get; } = variantId;
    public int Available { get; } = available;
    public int Threshold { get; } = threshold;
}
