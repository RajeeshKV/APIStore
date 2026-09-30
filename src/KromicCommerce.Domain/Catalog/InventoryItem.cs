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

    /// <summary>
    /// Reports whether <see cref="FinalizeReservation"/> would currently succeed for
    /// <paramref name="quantity"/>, without changing anything.
    ///
    /// Exists so a caller consuming a multi-line order can validate every line before
    /// mutating any of them. Without it, consuming line 1 and then failing on line 2 leaves a
    /// half-applied change that the caller can only abandon by not persisting — which is
    /// fragile, because the loaded entities stay dirty in the change tracker.
    /// </summary>
    public bool CanFinalizeReservation(int quantity) =>
        quantity > 0 && Reserved >= quantity;

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

    /// <summary>
    /// Returns <paramref name="quantity"/> units to sellable stock when an order is cancelled.
    ///
    /// A unit can be in either of two states at cancellation time, and the two need different
    /// bookkeeping:
    ///   - Still <b>reserved</b> (order was never confirmed): the unit is inside OnHand and must
    ///     come out of Reserved only.
    ///   - Already <b>finalised</b> (order was confirmed, so the unit was deducted from OnHand
    ///     by FinalizeReservation): the unit must be added back to OnHand.
    ///
    /// Calling plain <see cref="Release"/> for a finalised order throws, because Reserved is
    /// already 0 — which previously left a cancelled-and-refunded confirmed order with its
    /// units permanently missing from sellable stock. This method handles both states in one
    /// call so callers do not need to know which one they are in.
    ///
    /// Throws if more units are restored than the order could have consumed
    /// (<c>OnHand + quantity</c> would overflow), which guards against a double-apply.
    /// </summary>
    /// <summary>
    /// Reports whether <see cref="RestoreForCancellation"/> would currently succeed, without
    /// changing anything. Lets a caller validate every line of a multi-line order before
    /// restoring any of them, so a later failure cannot leave a half-applied restore.
    /// </summary>
    public bool CanRestoreForCancellation(int quantity)
    {
        if (quantity <= 0) return false;
        var fromSold = quantity - Math.Min(quantity, Reserved);
        return OnHand <= int.MaxValue - fromSold;
    }

    public void RestoreForCancellation(int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be > 0.", nameof(quantity));

        // Units that were still reserved are already inside OnHand, so they only need to leave
        // the Reserved bucket. Anything beyond Reserved had been deducted by FinalizeReservation
        // and has to be physically put back on the shelf.
        var fromReserved = Math.Min(quantity, Reserved);
        var fromSold = quantity - fromReserved;

        if (OnHand > int.MaxValue - fromSold)
            throw new InvalidOperationException(
                "Restoring this quantity would overflow on-hand stock.");

        Reserved -= fromReserved;
        OnHand += fromSold;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetLowStockThreshold(int threshold)
    {
        if (threshold < 0)
            throw new ArgumentException("Threshold cannot be negative.", nameof(threshold));
        LowStockThreshold = threshold;
    }
}
