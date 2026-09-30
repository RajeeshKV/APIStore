namespace KromicCommerce.Domain.Orders;

/// <summary>
/// The inventory lifecycle state of a single <see cref="OrderItem"/>. Persisted as a string.
///
/// This is the historical record of what actually happened to the units for one order line.
/// It exists because the CURRENT contents of an <c>InventoryItem</c> cannot tell you what an
/// order did: by the time a cancellation runs, other orders may have reserved or released stock
/// and a merchant may have restocked, so inferring "was this already given back?" from
/// <c>OnHand</c>/<c>Reserved</c> is unsound.
///
/// Lifecycle:
///   Untracked  — this line has no stock tracking (legacy product with no inventory row).
///                Nothing to reserve, finalise, or restore.
///   Reserved   — checkout held the units. They are inside OnHand but not sellable.
///   Finalized  — the order was confirmed; units were deducted from OnHand and are sold.
///   Restored   — the units were returned to sellable stock. Terminal; restoring again is
///                both impossible (the state forbids it) and unnecessary (the quantity was
///                already accounted for).
///
/// The transition Restored -> anything else is deliberately unreachable, which is what makes
/// cancellation idempotent rather than merely idempotent-by-convention.
/// </summary>
public enum OrderItemInventoryStatus
{
    /// <summary>No inventory row backs this line; no stock was ever affected.</summary>
    Untracked = 0,

    /// <summary>Units are held in <c>InventoryItem.Reserved</c>.</summary>
    Reserved = 1,

    /// <summary>Units were deducted from <c>InventoryItem.OnHand</c> (sold).</summary>
    Finalized = 2,

    /// <summary>Units were returned to sellable stock. Terminal.</summary>
    Restored = 3
}
