using KromicCommerce.Domain.Orders;

namespace KromicCommerce.UnitTests.Domain;

public sealed class OrderDomainTests
{
    private static ShippingAddress DefaultAddress() =>
        ShippingAddress.Create("Jane Doe", "+919876543210", "123 Street", null,
            "Mumbai", "Maharashtra", "400001", "IN");

    private static Order CreateTestOrder() =>
        Order.Create(Guid.NewGuid(), "ORD-TEST-001", "INR",
            500m, 50m, 0m, 0m, 0m, 550m, DefaultAddress(), PaymentMethod.Razorpay);

    // -----------------------------------------------------------------------
    // State machine
    // -----------------------------------------------------------------------

    [Fact]
    public void Order_starts_as_PendingPayment()
    {
        var order = CreateTestOrder();
        order.Status.Should().Be(OrderStatus.PendingPayment);
    }

    [Fact]
    public void Confirm_transitions_from_PaymentProcessing_to_Confirmed()
    {
        var order = CreateTestOrder();
        order.MarkPaymentProcessing();
        order.Confirm();
        order.Status.Should().Be(OrderStatus.Confirmed);
        order.PaidAt.Should().NotBeNull();
    }

    [Fact]
    public void Cannot_confirm_directly_from_PendingPayment()
    {
        var order = CreateTestOrder(); // PendingPayment
        var act = () => order.Confirm(); // skips PaymentProcessing
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MarkShipped_sets_tracking_info()
    {
        var order = CreateTestOrder();
        order.MarkPaymentProcessing();
        order.Confirm();
        order.MarkProcessing();
        order.MarkPacked();
        order.MarkShipped("TRK123", "Delhivery");
        order.Status.Should().Be(OrderStatus.Shipped);
        order.TrackingNumber.Should().Be("TRK123");
        order.TrackingProvider.Should().Be("Delhivery");
        order.ShippedAt.Should().NotBeNull();
    }

    [Fact]
    public void Cancel_allowed_from_Confirmed()
    {
        var order = CreateTestOrder();
        order.MarkPaymentProcessing();
        order.Confirm();
        order.Cancel("Customer request");
        order.Status.Should().Be(OrderStatus.Cancelled);
        order.CancellationReason.Should().Be("Customer request");
        order.CancelledAt.Should().NotBeNull();
    }

    [Fact]
    public void Cannot_cancel_shipped_order()
    {
        var order = CreateTestOrder();
        order.MarkPaymentProcessing();
        order.Confirm();
        order.MarkProcessing();
        order.MarkPacked();
        order.MarkShipped();
        var act = () => order.Cancel();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Cancel_is_idempotent_when_already_cancelled()
    {
        var order = CreateTestOrder();
        order.Cancel();
        var act = () => order.Cancel(); // second cancel from Cancelled state
        act.Should().Throw<InvalidOperationException>(); // no valid transition from Cancelled
    }

    [Fact]
    public void MarkDelivered_sets_delivered_at()
    {
        var order = CreateTestOrder();
        order.MarkPaymentProcessing();
        order.Confirm();
        order.MarkProcessing();
        order.MarkPacked();
        order.MarkShipped();
        order.MarkDelivered();
        order.Status.Should().Be(OrderStatus.Delivered);
        order.DeliveredAt.Should().NotBeNull();
    }

    [Fact]
    public void OrderCreated_domain_event_raised()
    {
        var order = CreateTestOrder();
        order.DomainEvents.OfType<OrderCreatedEvent>()
            .Should().ContainSingle();
    }

    [Fact]
    public void OrderStatusChanged_event_raised_on_transition()
    {
        var order = CreateTestOrder();
        order.ClearDomainEvents();
        order.MarkPaymentProcessing();
        order.DomainEvents.OfType<OrderStatusChangedEvent>()
            .Should().ContainSingle(e => e.NewStatus == OrderStatus.PaymentProcessing);
    }

    // -----------------------------------------------------------------------
    // ShippingAddress
    // -----------------------------------------------------------------------

    [Fact]
    public void ShippingAddress_Create_validates_required_fields()
    {
        var act = () => ShippingAddress.Create("", "+91999", "Line1", null, "City", "State", "12345", "IN");
        act.Should().Throw<ArgumentException>().WithMessage("*Full name*");
    }

    [Fact]
    public void ShippingAddress_equality_by_value()
    {
        var a = DefaultAddress();
        var b = DefaultAddress();
        a.Should().Be(b);
    }
}
