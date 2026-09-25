namespace KromicCommerce.Domain.Orders;

/// <summary>
/// Historical snapshot of a product line in an order.
/// All name, price, and SKU values are captured at checkout time and never
/// updated — so the order history remains accurate even if catalog data changes.
/// Money uses decimal (never double/float).
/// </summary>
public sealed class OrderItem : Entity
{
    private OrderItem() { } // EF constructor

    public static OrderItem Create(
        Guid orderId,
        Guid productId,
        Guid? variantId,
        string productName,
        string? variantDescription,
        string? sku,
        decimal unitPrice,
        int quantity)
    {
        if (string.IsNullOrWhiteSpace(productName))
            throw new ArgumentException("Product name is required for order history.", nameof(productName));
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be > 0.", nameof(quantity));
        if (unitPrice < 0)
            throw new ArgumentException("Unit price must be >= 0.", nameof(unitPrice));

        return new OrderItem
        {
            OrderId = orderId,
            ProductId = productId,
            VariantId = variantId,
            ProductName = productName,
            VariantDescription = variantDescription,
            Sku = sku,
            UnitPrice = unitPrice,
            Quantity = quantity,
            LineTotal = unitPrice * quantity
        };
    }

    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid? VariantId { get; private set; }

    // Snapshot fields — immutable after creation
    public string ProductName { get; private set; } = string.Empty;
    public string? VariantDescription { get; private set; }
    public string? Sku { get; private set; }

    /// <summary>Price captured at checkout. Never updated from current catalog.</summary>
    public decimal UnitPrice { get; private set; }

    public int Quantity { get; private set; }
    public decimal LineTotal { get; private set; }

    // Navigation
    public Order Order { get; private set; } = null!;
}
