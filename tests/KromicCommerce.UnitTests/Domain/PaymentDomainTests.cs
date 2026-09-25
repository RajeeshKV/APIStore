using KromicCommerce.Domain.Orders;

namespace KromicCommerce.UnitTests.Domain;

public sealed class PaymentDomainTests
{
    private static Payment Create() =>
        Payment.Create(Guid.NewGuid(), "Razorpay", 550m, "INR");

    [Fact]
    public void Payment_starts_as_Pending()
    {
        var p = Create();
        p.Status.Should().Be(PaymentStatus.Pending);
    }

    [Fact]
    public void MarkPaid_transitions_to_Paid_and_raises_event()
    {
        var p = Create();
        p.MarkPaid("pay_abc123");
        p.Status.Should().Be(PaymentStatus.Paid);
        p.ProviderPaymentId.Should().Be("pay_abc123");
        p.PaidAt.Should().NotBeNull();
        p.DomainEvents.OfType<PaymentSucceededEvent>().Should().ContainSingle();
    }

    [Fact]
    public void MarkFailed_transitions_to_Failed_and_raises_event()
    {
        var p = Create();
        p.MarkFailed("Insufficient funds");
        p.Status.Should().Be(PaymentStatus.Failed);
        p.FailureReason.Should().Be("Insufficient funds");
        p.DomainEvents.OfType<PaymentFailedEvent>().Should().ContainSingle();
    }

    [Fact]
    public void SetProviderOrderId_sets_the_field()
    {
        var p = Create();
        p.SetProviderOrderId("order_xyz");
        p.ProviderOrderId.Should().Be("order_xyz");
    }

    [Fact]
    public void Payment_Create_throws_for_negative_amount()
    {
        var act = () => Payment.Create(Guid.NewGuid(), "Razorpay", -1m, "INR");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Payment_Create_accepts_zero_amount()
    {
        var p = Payment.Create(Guid.NewGuid(), "CashOnDelivery", 0m, "INR");
        p.Amount.Should().Be(0m);
    }

    [Fact]
    public void PaymentCreated_event_raised_on_create()
    {
        var p = Create();
        p.DomainEvents.OfType<PaymentCreatedEvent>().Should().ContainSingle();
    }
}
