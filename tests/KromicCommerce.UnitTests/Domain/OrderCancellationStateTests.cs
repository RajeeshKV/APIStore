namespace KromicCommerce.UnitTests.Domain;

/// <summary>
/// Order state-machine rules that the refund-before-cancel workflow depends on.
///
/// Two transitions carry the whole design:
///   - Cancelled -> Refunded, because Razorpay refunds settle asynchronously. Cancelling first
///     and refunding later would otherwise leave a cancelled order with no legal path to ever
///     report the money as returned.
///   - CanCancel, so a caller that is about to issue an irreversible refund can check the
///     transition is legal BEFORE the money moves, instead of discovering afterwards that the
///     refund succeeded and the cancellation threw.
/// </summary>
public sealed class OrderCancellationStateTests
{
    private static Order BuildOrder(
        PaymentMethod paymentMethod = PaymentMethod.Razorpay,
        decimal grandTotal = 1000m)
    {
        var address = ShippingAddress.Create(
            "Test User", "+919876543210", "Line 1", null,
            "City", "State", "12345", "IN");

        var order = Order.Create(
            Guid.NewGuid(), "ORD-TEST", "INR",
            subtotal: grandTotal,
            shippingAmount: 0m,
            codFee: 0m,
            discountAmount: 0m,
            taxAmount: 0m,
            grandTotal: grandTotal,
            address,
            paymentMethod);

        typeof(KromicCommerce.Domain.Common.Entity)
            .GetProperty("Id")!.SetValue(order, Guid.NewGuid());

        return order;
    }

    // -----------------------------------------------------------------------
    // CanCancel
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatus.OrderPlaced)]
    [InlineData(OrderStatus.PendingPayment)]
    [InlineData(OrderStatus.PaymentProcessing)]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Processing)]
    [InlineData(OrderStatus.Packed)]
    public void CanCancel_is_true_for_every_cancellable_status(OrderStatus status)
    {
        var order = DriveTo(BuildOrder(), status);

        order.CanCancel.Should().BeTrue();
    }

    /// <summary>
    /// A shipped or delivered order is past the point where cancelling is meaningful, and
    /// refunding it is a different operation entirely.
    /// </summary>
    [Theory]
    [InlineData(OrderStatus.Shipped)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.RefundPending)]
    [InlineData(OrderStatus.Refunded)]
    [InlineData(OrderStatus.Failed)]
    public void CanCancel_is_false_once_cancellation_is_no_longer_a_valid_outcome(OrderStatus status)
    {
        var order = DriveTo(BuildOrder(), status);

        order.CanCancel.Should().BeFalse();
    }

    /// <summary>
    /// Cancelled -> Refunded must be legal. Razorpay refunds settle asynchronously
    /// (refund.processed / refund.failed webhooks), so the settlement handler has to be able
    /// to move a cancelled order to Refunded once the provider confirms.
    /// </summary>
    [Fact]
    public void A_cancelled_order_can_still_reach_Refunded()
    {
        var order = BuildOrder();
        order.Confirm();
        order.Cancel("customer request");

        var act = () => order.MarkRefunded();

        act.Should().NotThrow();
        order.Status.Should().Be(OrderStatus.Refunded);
    }

    [Fact]
    public void Cancelling_records_the_reason_and_timestamp()
    {
        var order = BuildOrder();
        order.Confirm();

        order.Cancel("changed my mind");

        order.Status.Should().Be(OrderStatus.Cancelled);
        order.CancellationReason.Should().Be("changed my mind");
        order.CancelledAt.Should().NotBeNull();
    }

    [Fact]
    public void Cancelling_twice_is_rejected()
    {
        var order = BuildOrder();
        order.Confirm();
        order.Cancel("first");

        var act = () => order.Cancel("second");

        act.Should().Throw<InvalidOperationException>();
        order.Status.Should().Be(OrderStatus.Cancelled);
    }

    /// <summary>
    /// Refunded is terminal: an order whose money came back cannot be cancelled, shipped or
    /// re-refunded. Without this, a settled refund could be followed by a cancellation that
    /// tries to issue a second one.
    /// </summary>
    [Fact]
    public void A_refunded_order_is_terminal()
    {
        var order = BuildOrder();
        order.Confirm();
        order.Cancel("refund path");
        order.MarkRefunded();
        order.Status.Should().Be(OrderStatus.Refunded);

        var act = () => order.MarkRefundPending();

        act.Should().Throw<InvalidOperationException>();
        order.Status.Should().Be(OrderStatus.Refunded);
    }

    /// <summary>
    /// A cancelled order is already on the way out, so the only way further is to report the
    /// refund settling. Cancelling again or returning to a fulfillment state must both fail.
    /// </summary>
    [Fact]
    public void A_cancelled_order_can_only_move_to_Refunded()
    {
        var order = BuildOrder();
        order.Confirm();
        order.Cancel("customer request");

        var cancelAgain = () => order.Cancel("customer changed their mind again");
        var resumeFulfillment = () => order.MarkProcessing();

        cancelAgain.Should().Throw<InvalidOperationException>();
        resumeFulfillment.Should().Throw<InvalidOperationException>();
        order.Status.Should().Be(OrderStatus.Cancelled);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Walks a fresh order along the only legal path to the requested status. Each step
    /// asserts its own legality so a broken transition surfaces as a clear failure here
    /// rather than as a confusing one in the test body.
    /// </summary>
    private static Order DriveTo(Order order, OrderStatus status)
    {
        switch (status)
        {
            case OrderStatus.OrderPlaced:
                break;
            case OrderStatus.PendingPayment:
                order.MarkPendingPayment();
                break;
            case OrderStatus.PaymentProcessing:
                order.MarkPendingPayment();
                order.MarkPaymentProcessing();
                break;
            case OrderStatus.Confirmed:
                order.Confirm();
                break;
            case OrderStatus.Processing:
                order.Confirm();
                order.MarkProcessing();
                break;
            case OrderStatus.Packed:
                order.Confirm();
                order.MarkProcessing();
                order.MarkPacked();
                break;
            case OrderStatus.Shipped:
                order.Confirm();
                order.MarkProcessing();
                order.MarkPacked();
                order.MarkShipped("TRK-1", "Delhivery");
                break;
            case OrderStatus.Delivered:
                order.Confirm();
                order.MarkProcessing();
                order.MarkPacked();
                order.MarkShipped("TRK-1", "Delhivery");
                order.MarkDelivered();
                break;
            case OrderStatus.RefundPending:
                order.Confirm();
                order.MarkProcessing();
                order.MarkPacked();
                order.MarkShipped("TRK-1", "Delhivery");
                order.MarkDelivered();
                order.MarkRefundPending();
                break;
            case OrderStatus.Refunded:
                order.Confirm();
                order.MarkProcessing();
                order.MarkPacked();
                order.MarkShipped("TRK-1", "Delhivery");
                order.MarkDelivered();
                order.MarkRefundPending();
                order.MarkRefunded();
                break;
            case OrderStatus.Cancelled:
                order.Confirm();
                order.Cancel("cancelled");
                break;
            case OrderStatus.Failed:
                order.MarkFailed();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }

        order.Status.Should().Be(status);
        return order;
    }
}
