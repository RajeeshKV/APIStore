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

    // Helper: walk a Razorpay order through to Confirmed
    private static Order CreateConfirmedOrder()
    {
        var order = CreateTestOrder();    // OrderPlaced
        order.MarkPendingPayment();       // → PendingPayment
        order.MarkPaymentReceived();      // → OrderPlaced (paid)
        order.Confirm();                  // → Confirmed
        return order;
    }

    // -----------------------------------------------------------------------
    // State machine
    // -----------------------------------------------------------------------

    [Fact]
    public void Order_starts_as_OrderPlaced()
    {
        var order = CreateTestOrder();
        order.Status.Should().Be(OrderStatus.OrderPlaced);
    }

    [Fact]
    public void Order_sets_OrderPlacedAt_on_creation()
    {
        var order = CreateTestOrder();
        order.OrderPlacedAt.Should().NotBeNull();
    }

    [Fact]
    public void Razorpay_order_transitions_through_payment_flow()
    {
        var order = CreateTestOrder();
        order.MarkPendingPayment();
        order.Status.Should().Be(OrderStatus.PendingPayment);

        order.MarkPaymentReceived();
        order.Status.Should().Be(OrderStatus.OrderPlaced);
        order.PaidAt.Should().NotBeNull();
    }

    [Fact]
    public void Confirm_transitions_from_OrderPlaced_to_Confirmed()
    {
        var order = CreateTestOrder(); // OrderPlaced
        order.Confirm();
        order.Status.Should().Be(OrderStatus.Confirmed);
    }

    [Fact]
    public void Confirm_transitions_from_PaymentProcessing_to_Confirmed()
    {
        var order = CreateTestOrder();
        order.MarkPendingPayment();
        order.MarkPaymentProcessing();
        order.Confirm();
        order.Status.Should().Be(OrderStatus.Confirmed);
    }

    [Fact]
    public void MarkShipped_sets_tracking_info()
    {
        var order = CreateConfirmedOrder();
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
        var order = CreateConfirmedOrder();
        order.Cancel("Customer request");
        order.Status.Should().Be(OrderStatus.Cancelled);
        order.CancellationReason.Should().Be("Customer request");
        order.CancelledAt.Should().NotBeNull();
    }

    [Fact]
    public void Cannot_cancel_shipped_order()
    {
        var order = CreateConfirmedOrder();
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
        var act = () => order.Cancel();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MarkDelivered_sets_delivered_at()
    {
        var order = CreateConfirmedOrder();
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
        order.MarkPendingPayment();
        order.DomainEvents.OfType<OrderStatusChangedEvent>()
            .Should().ContainSingle(e => e.NewStatus == OrderStatus.PendingPayment);
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
