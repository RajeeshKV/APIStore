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
    ///
    /// PRE-CHECK ONLY. It reads the current Reserved value and is subject to a
    /// time-of-check/time-of-use race. The actual concurrency guarantee is the xmin token on
    /// this entity: <see cref="FinalizeReservation"/> mutates in memory, and the UPDATE it
    /// produces is rejected by PostgreSQL if another transaction changed this row in between,
    /// raising <c>DbUpdateConcurrencyException</c>.
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
    /// Reports whether <see cref="RestoreSoldUnits"/> would currently succeed, without
    /// changing anything. Lets a caller validate every line of a multi-line order before
    /// restoring any of them, so a later failure cannot leave a half-applied restore.
    ///
    /// This is a PRE-CHECK ONLY. It reads current numbers and is therefore subject to a
    /// time-of-check/time-of-use race; <see cref="RestoreSoldUnits"/> is the operation that
    /// actually moves stock, and the write itself is guarded by the xmin concurrency token.
    /// </summary>
    public bool CanRestoreSoldUnits(int quantity)
    {
        if (quantity <= 0) return false;
        return OnHand <= int.MaxValue - quantity;
    }

    /// <summary>
    /// Returns <paramref name="quantity"/> physically-sold units to sellable stock.
    ///
    /// Use ONLY for units that were previously deducted from OnHand by
    /// <see cref="FinalizeReservation"/> — that is, for an order line whose persisted
    /// <c>OrderItem.InventoryStatus</c> is Finalized.
    ///
    /// The caller MUST pass the quantity that was finalized for that specific order line
    /// (<c>OrderItem.InventoryQuantity</c>). This method deliberately does NOT look at
    /// <see cref="Reserved"/> to work out how much to put back. The previous implementation
    /// inferred the split (<c>fromReserved = min(qty, Reserved)</c>, remainder sold), which
    /// silently misattributed any unrelated reservation that happened to be sitting in the
    /// Reserved bucket to this order — restoring units that belonged to somebody else and
    /// stranding this order's own units. The historical split now lives on the order line,
    /// where it is unambiguous and per-order.
    ///
    /// Idempotency is the caller's responsibility (via <c>OrderItem.CanRestoreInventory</c>),
    /// because only the order line knows whether these units were already returned.
    /// </summary>
    public void RestoreSoldUnits(int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be > 0.", nameof(quantity));

        if (OnHand > int.MaxValue - quantity)
            throw new InvalidOperationException(
                "Restoring this quantity would overflow on-hand stock.");

        OnHand += quantity;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetLowStockThreshold(int threshold)
    {
        if (threshold < 0)
            throw new ArgumentException("Threshold cannot be negative.", nameof(threshold));
        LowStockThreshold = threshold;
    }
}
