namespace KromicCommerce.Domain.Catalog;

/// <summary>
/// Tracks stock for a product or specific product variant.
/// VariantId null = base product stock (used when product has no variants).
///
/// Concurrency: RowVersion is a PostgreSQL xmin column used as a concurrency token.
/// This prevents overselling when multiple requests modify stock simultaneously.
///
/// Available = OnHand - Reserved  (computed, not persisted — always derived)
/// </summary>
public sealed class InventoryItem : Entity
{
    private InventoryItem() { } // EF constructor

    public static InventoryItem Create(
        Guid productId,
        Guid? variantId,
        int onHand,
        int lowStockThreshold = 5)
    {
        if (onHand < 0)
            throw new ArgumentException("On-hand stock cannot be negative.", nameof(onHand));
        if (lowStockThreshold < 0)
            throw new ArgumentException("Low-stock threshold cannot be negative.", nameof(lowStockThreshold));

        return new InventoryItem
        {
            ProductId = productId,
            VariantId = variantId,
            OnHand = onHand,
            Reserved = 0,
            LowStockThreshold = lowStockThreshold,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public Guid ProductId { get; private set; }

    /// <summary>Null = applies to the base product. Non-null = variant-specific stock.</summary>
    public Guid? VariantId { get; private set; }

    /// <summary>Physical units in warehouse.</summary>
    public int OnHand { get; private set; }

    /// <summary>Units held for in-progress checkouts/orders. Must not exceed OnHand.</summary>
    public int Reserved { get; private set; }

    /// <summary>Available for purchase = OnHand - Reserved.</summary>
    public int Available => OnHand - Reserved;

    public int LowStockThreshold { get; private set; }
    public bool IsLowStock => Available <= LowStockThreshold && Available > 0;
    public bool IsOutOfStock => Available <= 0;

    public DateTime UpdatedAt { get; private set; }

    /// <summary>EF Core concurrency token (maps to PostgreSQL xmin).</summary>
    public uint RowVersion { get; private set; }

    // Navigation
    public Product Product { get; private set; } = null!;
    public ProductVariant? Variant { get; private set; }

    // -----------------------------------------------------------------------
    // Behaviour
    // -----------------------------------------------------------------------

    public void SetOnHand(int onHand)
    {
        if (onHand < 0)
            throw new ArgumentException("On-hand stock cannot be negative.", nameof(onHand));
        if (onHand < Reserved)
            throw new InvalidOperationException(
                $"Cannot set on-hand to {onHand}; {Reserved} units are currently reserved.");

        OnHand = onHand;
        UpdatedAt = DateTime.UtcNow;
    }

    public void AdjustOnHand(int delta)
    {
        var newValue = OnHand + delta;
        if (newValue < 0)
            throw new InvalidOperationException(
                $"Stock adjustment would result in negative on-hand ({newValue}).");
        if (newValue < Reserved)
            throw new InvalidOperationException(
                $"Stock adjustment would leave fewer units than currently reserved ({Reserved}).");

        OnHand = newValue;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Reserve(int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentException("Reservation quantity must be > 0.", nameof(quantity));
        if (quantity > Available)
            throw new InvalidOperationException(
                $"Cannot reserve {quantity}; only {Available} units available.");

        Reserved += quantity;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Release(int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentException("Release quantity must be > 0.", nameof(quantity));
        if (quantity > Reserved)
            throw new InvalidOperationException(
                $"Cannot release {quantity}; only {Reserved} units reserved.");

        Reserved -= quantity;
        UpdatedAt = DateTime.UtcNow;
    }

    public void FinalizeReservation(int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be > 0.", nameof(quantity));
        if (quantity > Reserved)
            throw new InvalidOperationException(
                $"Cannot finalize {quantity}; only {Reserved} reserved.");

        Reserved -= quantity;
        OnHand -= quantity;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetLowStockThreshold(int threshold)
    {
        if (threshold < 0)
            throw new ArgumentException("Threshold cannot be negative.", nameof(threshold));
        LowStockThreshold = threshold;
    }
}
