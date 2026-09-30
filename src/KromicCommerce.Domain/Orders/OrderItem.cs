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

    // -----------------------------------------------------------------------
    // Inventory lifecycle
    //
    // This is the persisted, order-specific record of what happened to the units for
    // THIS line. Restoration is driven from here, never inferred from the current
    // OnHand/Reserved numbers — see OrderItemInventoryStatus for why that is unsound.
    // -----------------------------------------------------------------------

    /// <summary>Where this line's units currently sit in the inventory lifecycle.</summary>
    public OrderItemInventoryStatus InventoryStatus { get; private set; } = OrderItemInventoryStatus.Untracked;

    /// <summary>
    /// Exact number of units this line placed under inventory control. Recorded when the
    /// units are reserved and reused verbatim when they are finalised and restored, so a
    /// cancellation can never return a different number than was taken.
    /// </summary>
    public int InventoryQuantity { get; private set; }

    /// <summary>
    /// PostgreSQL xmin concurrency token. Two cancellation requests that both load this line
    /// in <see cref="OrderItemInventoryStatus.Finalized"/> will try to write it; the loser's
    /// UPDATE is rejected by the database and surfaces as a DbUpdateConcurrencyException
    /// instead of restoring the same units twice.
    /// </summary>
    public uint Version { get; private set; }

    /// <summary>True when this line actually controls stock (i.e. a row backs it).</summary>
    public bool TracksInventory => InventoryStatus != OrderItemInventoryStatus.Untracked;

    /// <summary>
    /// Records that checkout took <paramref name="quantity"/> units into Reserved.
    /// Called once, at order creation.
    /// </summary>
    public void MarkInventoryReserved(int quantity)
    {
        if (InventoryStatus != OrderItemInventoryStatus.Untracked)
            throw new InvalidOperationException(
                $"Order line {Id} already has inventory status {InventoryStatus}; " +
                "a line can only be reserved once.");

        if (quantity <= 0)
            throw new ArgumentException("Reserved quantity must be > 0.", nameof(quantity));

        InventoryQuantity = quantity;
        InventoryStatus = OrderItemInventoryStatus.Reserved;
    }

    /// <summary>Marks this line as not stock-tracked. Called when no inventory row backs it.</summary>
    public void MarkInventoryUntracked()
    {
        InventoryStatus = OrderItemInventoryStatus.Untracked;
        InventoryQuantity = 0;
    }

    /// <summary>
    /// Marks the reserved units as sold. Moves Reserved -> Finalized.
    /// </summary>
    public void MarkInventoryFinalized()
    {
        if (InventoryStatus != OrderItemInventoryStatus.Reserved)
            throw new InvalidOperationException(
                $"Order line {Id} is {InventoryStatus}, not Reserved; " +
                "only reserved units can be finalized.");

        InventoryStatus = OrderItemInventoryStatus.Finalized;
    }

    /// <summary>
    /// Whether these units still need returning to sellable stock. False once restored, which
    /// is what stops a second cancellation from restoring them again.
    /// </summary>
    public bool CanRestoreInventory =>
        InventoryStatus is OrderItemInventoryStatus.Reserved
            or OrderItemInventoryStatus.Finalized;

    /// <summary>
    /// Marks the units as returned to sellable stock. Moves Reserved|Finalized -> Restored.
    /// Throws if already restored, so a duplicate call is a loud error rather than a
    /// silent double-restore.
    /// </summary>
    public void MarkInventoryRestored()
    {
        if (!CanRestoreInventory)
            throw new InvalidOperationException(
                $"Order line {Id} inventory is {InventoryStatus}; there is nothing to restore.");

        InventoryStatus = OrderItemInventoryStatus.Restored;
    }

    // Navigation
    public Order Order { get; private set; } = null!;
}
