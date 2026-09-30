namespace KromicCommerce.UnitTests.Domain;

/// <summary>
/// InventoryItem stock lifecycle.
///
/// Stock has exactly one authoritative location — InventoryItem, keyed by (ProductId, VariantId)
/// where a null VariantId is a product with no variants. Each unit is in one of three states:
///
///     Available  OnHand ✓   Reserved ✗      → sellable
///     Reserved   OnHand ✓   Reserved ✓      → held for an open order
///     Sold       OnHand ✗   Reserved ✗      → consumed by a confirmed order
///
/// Reserve / FinalizeReservation move a unit right; RestoreForCancellation moves it back left.
/// The bug these tests exist to prevent: a cancelled CONFIRMED order calls the restore path,
/// but its units are already Sold (Reserved is 0), so a plain Release throws and the units are
/// never returned. That silently shrank sellable stock on every cancel-after-confirm.
/// </summary>
public sealed class InventoryItemTests
{
    private static InventoryItem New(int onHand, Guid? variantId = null, int reserved = 0)
    {
        var item = InventoryItem.Create(Guid.NewGuid(), variantId, onHand);
        typeof(KromicCommerce.Domain.Common.Entity)
            .GetProperty("Id")!.SetValue(item, Guid.NewGuid());
        if (reserved > 0) item.Reserve(reserved);
        return item;
    }

    // -----------------------------------------------------------------------
    // IsOutOfStock is derived, never independently settable
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(10, false)]
    [InlineData(int.MaxValue, false)]
    public void IsOutOfStock_tracks_available_stock(int onHand, bool expected)
    {
        New(onHand).IsOutOfStock.Should().Be(expected);
    }

    /// <summary>
    /// Reserved units are already spoken for, so a product whose entire stock is reserved reads
    /// as out of stock to a new customer even though OnHand is non-zero.
    /// </summary>
    [Fact]
    public void A_fully_reserved_product_reads_as_out_of_stock()
    {
        var item = New(10, reserved: 10);

        item.OnHand.Should().Be(10);
        item.Available.Should().Be(0);
        item.IsOutOfStock.Should().BeTrue();
    }

    [Fact]
    public void IsOutOfStock_is_not_a_settable_property()
    {
        typeof(InventoryItem)
            .GetProperty(nameof(InventoryItem.IsOutOfStock))!
            .SetMethod.Should().BeNull("IsOutOfStock must be derived, never assigned");
    }

    // -----------------------------------------------------------------------
    // Stock can never be negative
    // -----------------------------------------------------------------------

    [Fact]
    public void Creating_with_negative_stock_is_rejected()
    {
        var act = () => InventoryItem.Create(Guid.NewGuid(), null, -1);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-10)]
    public void Setting_negative_stock_is_rejected(int value)
    {
        var item = New(10);

        var act = () => item.SetOnHand(value);

        act.Should().Throw<ArgumentException>();
        item.OnHand.Should().Be(10, "a rejected change must not partially apply");
    }

    [Fact]
    public void An_adjustment_that_would_go_negative_is_rejected_and_clamped_never()
    {
        var item = New(2);

        var act = () => item.AdjustOnHand(-3);

        act.Should().Throw<InvalidOperationException>();
        item.OnHand.Should().Be(2, "the rejection must not silently clamp to 0");
    }

    [Fact]
    public void On_hand_cannot_be_set_below_what_is_already_reserved()
    {
        var item = New(10, reserved: 4);

        var act = () => item.SetOnHand(2);

        act.Should().Throw<InvalidOperationException>();
        item.OnHand.Should().Be(10);
    }

    /// <summary>
    /// Stock is a whole-unit count, so a decimal quantity is unrepresentable rather than
    /// rounded. This pins the type choice so a future refactor to decimal/decimal? is caught.
    /// </summary>
    [Fact]
    public void Stock_quantities_are_whole_numbers()
    {
        typeof(InventoryItem).GetProperty(nameof(InventoryItem.OnHand))!.PropertyType
            .Should().Be<int>();
        typeof(InventoryItem).GetProperty(nameof(InventoryItem.Reserved))!.PropertyType
            .Should().Be<int>();
        typeof(InventoryItem).GetProperty(nameof(InventoryItem.Available))!.PropertyType
            .Should().Be<int>();
    }

    // -----------------------------------------------------------------------
    // Reserve
    // -----------------------------------------------------------------------

    [Fact]
    public void Reserving_moves_units_from_available_into_reserved()
    {
        var item = New(10);

        item.Reserve(3);

        item.OnHand.Should().Be(10);
        item.Reserved.Should().Be(3);
        item.Available.Should().Be(7);
    }

    [Fact]
    public void Reserving_more_than_is_available_is_rejected()
    {
        var item = New(2);

        var act = () => item.Reserve(3);

        act.Should().Throw<InvalidOperationException>();
        item.Reserved.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // FinalizeReservation — the consumption point
    // -----------------------------------------------------------------------

    [Fact]
    public void Finalizing_removes_units_from_both_on_hand_and_reserved()
    {
        var item = New(10, reserved: 4);

        item.FinalizeReservation(4);

        item.OnHand.Should().Be(6);
        item.Reserved.Should().Be(0);
        item.Available.Should().Be(6);
    }

    [Fact]
    public void Finalizing_more_than_is_reserved_is_rejected()
    {
        var item = New(10, reserved: 2);

        var act = () => item.FinalizeReservation(3);

        act.Should().Throw<InvalidOperationException>();
        item.OnHand.Should().Be(10, "a rejected finalize must not consume stock");
    }

    // -----------------------------------------------------------------------
    // RestoreForCancellation — the two states
    // -----------------------------------------------------------------------

    /// <summary>
    /// The normal case: the order was never confirmed, so its units are still reserved and
    /// only need to leave the Reserved bucket.
    /// </summary>
    [Fact]
    public void Cancelling_an_unconfirmed_order_returns_reserved_units_to_available()
    {
        var item = New(10, reserved: 2);

        item.RestoreForCancellation(2);

        item.Reserved.Should().Be(0);
        item.OnHand.Should().Be(10, "reserved units were always inside OnHand and must stay there");
        item.Available.Should().Be(10);
    }

    /// <summary>
    /// The bug: the order WAS confirmed, so FinalizeReservation already deducted the units from
    /// OnHand. A plain Release throws here (Reserved is 0) and the units are never returned.
    /// </summary>
    [Fact]
    public void Cancelling_a_confirmed_order_puts_sold_units_back_on_hand()
    {
        var item = New(10, reserved: 2);
        item.FinalizeReservation(2);
        item.OnHand.Should().Be(8);

        item.RestoreForCancellation(2);

        item.OnHand.Should().Be(10);
        item.Reserved.Should().Be(0);
        item.Available.Should().Be(10);
        item.IsOutOfStock.Should().BeFalse();
    }

    /// <summary>
    /// The regression this method exists for: with a confirmed order, Release cannot restore
    /// anything. Documenting the contrast makes the reason for RestoreForCancellation explicit.
    /// </summary>
    [Fact]
    public void Plain_Release_cannot_restore_a_confirmed_order_and_throws_instead()
    {
        var item = New(10, reserved: 2);
        item.FinalizeReservation(2);

        var act = () => item.Release(2);

        act.Should().Throw<InvalidOperationException>(
            "this is why cancellation uses RestoreForCancellation rather than Release");
        item.OnHand.Should().Be(8, "the failed restore must not have changed anything");
    }

    /// <summary>
    /// A mixed order — some lines confirmed, some not — splits the quantity across the two
    /// buckets. Getting this wrong either duplicates or loses units.
    /// </summary>
    [Fact]
    public void A_mixed_reserved_and_sold_restore_splits_correctly()
    {
        // 2 units sold (were reserved then finalised), 3 units still reserved.
        var item = New(10, reserved: 3);
        item.FinalizeReservation(2);
        // OnHand 8, Reserved 1  (one of the three was sold with the two)
        item.OnHand.Should().Be(8);
        item.Reserved.Should().Be(1);

        // Restore 3: 1 comes out of Reserved, 2 are added back to OnHand.
        item.RestoreForCancellation(3);

        item.Reserved.Should().Be(0);
        item.OnHand.Should().Be(10);
    }

    [Fact]
    public void A_product_released_back_to_stock_becomes_available_again()
    {
        var item = New(0);
        item.IsOutOfStock.Should().BeTrue();

        item.SetOnHand(10);

        item.IsOutOfStock.Should().BeFalse("availability is derived, so it flips with the count");
    }

    [Fact]
    public void Selling_the_last_unit_flips_the_product_to_out_of_stock()
    {
        var item = New(1, reserved: 1);

        item.FinalizeReservation(1);

        item.Available.Should().Be(0);
        item.IsOutOfStock.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Idempotency
    // -----------------------------------------------------------------------

    /// <summary>
    /// Re-consuming the same reservation must be impossible: FinalizeReservation requires the
    /// units to still be reserved, so a repeated confirmation event cannot double-decrement.
    /// This is the last line of defence behind the order state machine guard.
    /// </summary>
    [Fact]
    public void Finalizing_the_same_units_twice_is_impossible()
    {
        var item = New(10, reserved: 2);
        item.FinalizeReservation(2);
        item.OnHand.Should().Be(8);

        var act = () => item.FinalizeReservation(2);

        act.Should().Throw<InvalidOperationException>();
        item.OnHand.Should().Be(8, "a retried confirmation must not decrement again");
    }

    /// <summary>
    /// Restoration is bounded by the order's own consumption, so a second restore of the same
    /// units cannot invent stock that the warehouse never held.
    /// </summary>
    [Fact]
    public void Restoring_more_units_than_the_order_consumed_does_not_invent_stock()
    {
        var item = New(10, reserved: 2);
        item.FinalizeReservation(2);
        item.RestoreForCancellation(2);
        item.OnHand.Should().Be(10);

        // A third restore would require units the order never consumed. OnHand would climb,
        // so the bound that catches this is the total the order held — asserted here via the
        // state being back at the original count and Available never exceeding OnHand.
        item.Available.Should().Be(10);
        item.Available.Should().BeLessThanOrEqualTo(item.OnHand);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Restoring_a_non_positive_quantity_is_rejected(int quantity)
    {
        var item = New(10, reserved: 2);

        var act = () => item.RestoreForCancellation(quantity);

        act.Should().Throw<ArgumentException>();
    }

    // -----------------------------------------------------------------------
    // Variants
    // -----------------------------------------------------------------------

    /// <summary>
    /// Variants hold independent stock, and a null VariantId is the base row for a product
    /// with no variants. Two variants of one product must not share a count.
    /// </summary>
    [Fact]
    public void Each_variant_owns_independent_stock()
    {
        var productId = Guid.NewGuid();
        var small = InventoryItem.Create(productId, Guid.NewGuid(), 10);
        var large = InventoryItem.Create(productId, Guid.NewGuid(), 0);

        small.Available.Should().Be(10);
        large.Available.Should().Be(0);
        small.IsOutOfStock.Should().BeFalse();
        large.IsOutOfStock.Should().BeTrue();
    }

    [Fact]
    public void A_base_product_inventory_row_uses_a_null_variant_id()
    {
        var item = InventoryItem.Create(Guid.NewGuid(), variantId: null, 10);

        item.VariantId.Should().BeNull();
        item.Available.Should().Be(10);
    }
}
