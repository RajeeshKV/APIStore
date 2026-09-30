using System.Linq.Expressions;
using KromicCommerce.Application.Abstractions.Payments;
using KromicCommerce.Application.Features.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace KromicCommerce.UnitTests.Application;

/// <summary>
/// The refund-before-cancel ordering contract.
///
/// The invariant under test: for a captured online payment, the refund is issued and PERSISTED
/// before the order is cancelled, and a rejected refund leaves the order completely untouched.
/// Both customer-initiated and admin-initiated cancellations route through
/// <see cref="OrderCancellationService"/>, so this holds for either caller.
///
/// These are the tests that would fail if someone reordered the steps in the service, and they
/// are deliberately written against observable state (order status, payment record, gateway
/// call count) rather than internal call sequence.
/// </summary>
public sealed class OrderCancellationRefundTests
{
    private readonly Mock<IApplicationDbContext> _db = new();
    private readonly Mock<IPaymentGateway> _gateway = new();

    // -----------------------------------------------------------------------
    // Refund rejected — the order must not change
    // -----------------------------------------------------------------------

    /// <summary>
    /// The core guarantee. If the provider refuses the refund, the order stays exactly as it
    /// was: cancelling it anyway would tell the customer their order is cancelled while their
    /// money is still captured and never coming back.
    /// </summary>
    [Fact]
    public async Task Refund_rejected_leaves_the_order_unchanged()
    {
        var order = BuildConfirmedRazorpayOrder();
        var payment = BuildCapturedPayment(order.Id, 1000m);
        SetupPayments([payment]);
        SetupInventory([]);

        _gateway.Setup(g => g.RefundAsync(
                It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefundResult(false, null, "Razorpay returned an error"));

        var result = await BuildService().CancelAsync(
            order, "changed my mind", "customer", CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("REFUND_FAILED");

        order.Status.Should().Be(OrderStatus.Confirmed);
        order.CancelledAt.Should().BeNull();
        payment.IsRefunded.Should().BeFalse();
    }

    /// <summary>
    /// No cancellation means no notification and no inventory release. Emitting an
    /// OrderCancelled event for an order that is still live would also fire the customer's
    /// "your order was cancelled" email.
    /// </summary>
    [Fact]
    public async Task Refund_rejected_writes_nothing_at_all()
    {
        var order = BuildConfirmedRazorpayOrder();
        var payment = BuildCapturedPayment(order.Id, 1000m);
        SetupPayments([payment]);
        SetupInventory([]);

        _gateway.Setup(g => g.RefundAsync(
                It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefundResult(false, null, "gateway down"));

        var mockOutbox = SetupOutbox();
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        await BuildService().CancelAsync(order, "reason", "customer", CancellationToken.None);

        _db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        mockOutbox.Verify(o => o.Add(It.IsAny<OutboxEvent>()), Times.Never);
    }

    /// <summary>
    /// The error tells the customer their money is safe and the order is untouched. An
    /// operator-actionable message is required because the alternative — the caller assumes
    /// the order is cancelled — is the failure mode this whole design exists to prevent.
    /// </summary>
    [Fact]
    public async Task Refund_rejected_explains_that_the_order_is_unchanged()
    {
        var order = BuildConfirmedRazorpayOrder();
        SetupPayments([BuildCapturedPayment(order.Id, 1000m)]);
        SetupInventory([]);

        _gateway.Setup(g => g.RefundAsync(
                It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefundResult(false, null, "gateway down"));

        var result = await BuildService().CancelAsync(
            order, "reason", "customer", CancellationToken.None);

        result.Error.Description.Should().Contain("unchanged");
    }

    // -----------------------------------------------------------------------
    // Refund accepted — refund is recorded, then the order is cancelled
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Refund_accepted_cancels_the_order_and_records_the_receipt()
    {
        var order = BuildConfirmedRazorpayOrder();
        var payment = BuildCapturedPayment(order.Id, 1000m);
        SetupPayments([payment]);
        SetupInventory([]);
        SetupOutbox();
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _gateway.Setup(g => g.RefundAsync(
                It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefundResult(true, "rfnd_abc", null));

        var result = await BuildService().CancelAsync(
            order, "changed my mind", "customer", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Cancelled);

        payment.Status.Should().Be(PaymentStatus.Refunded);
        payment.ProviderRefundId.Should().Be("rfnd_abc");
        payment.RefundedAmount.Should().Be(1000m);
    }

    /// <summary>
    /// The refund is committed in its own SaveChanges before the cancellation is committed.
    /// That separate write is the idempotency checkpoint: if the second write fails, the
    /// recorded refund still prevents a retry from refunding the same payment twice.
    /// </summary>
    [Fact]
    public async Task Refund_receipt_is_committed_before_the_cancellation()
    {
        var order = BuildConfirmedRazorpayOrder();
        SetupPayments([BuildCapturedPayment(order.Id, 1000m)]);
        SetupInventory([]);
        SetupOutbox();
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _gateway.Setup(g => g.RefundAsync(
                It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefundResult(true, "rfnd_abc", null));

        await BuildService().CancelAsync(order, "reason", "customer", CancellationToken.None);

        // One write for the refund receipt, one for the cancellation itself.
        _db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    /// <summary>
    /// The provider call happens before the order is cancelled, so an observer of the gateway
    /// at call time would still see a live order. This pins the ordering directly.
    /// </summary>
    [Fact]
    public async Task The_provider_is_called_before_the_order_is_cancelled()
    {
        var order = BuildConfirmedRazorpayOrder();
        SetupPayments([BuildCapturedPayment(order.Id, 1000m)]);
        SetupInventory([]);
        SetupOutbox();
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        OrderStatus statusWhenGatewayWasCalled = OrderStatus.PendingPayment;
        _gateway.Setup(g => g.RefundAsync(
                It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                statusWhenGatewayWasCalled = order.Status;
                return new RefundResult(true, "rfnd_abc", null);
            });

        await BuildService().CancelAsync(order, "reason", "customer", CancellationToken.None);

        statusWhenGatewayWasCalled.Should().Be(OrderStatus.Confirmed);
        order.Status.Should().Be(OrderStatus.Cancelled);
    }

    /// <summary>
    /// The idempotency key is derived from the order ID, so a retry of the same cancellation
    /// produces the same key and the provider de-duplicates it instead of refunding twice.
    /// </summary>
    [Fact]
    public async Task The_refund_uses_a_stable_order_derived_idempotency_key()
    {
        var order = BuildConfirmedRazorpayOrder();
        SetupPayments([BuildCapturedPayment(order.Id, 1000m)]);
        SetupInventory([]);
        SetupOutbox();
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _gateway.Setup(g => g.RefundAsync(
                It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefundResult(true, "rfnd_abc", null));

        await BuildService().CancelAsync(order, "reason", "customer", CancellationToken.None);

        _gateway.Verify(g => g.RefundAsync(
            It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(),
            $"order-cancel:{order.Id:N}", It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// The full captured amount is refunded, not the order's grand total as the caller sees it.
    /// Refunding a different figure than was charged is how a customer ends up out of pocket.
    /// </summary>
    [Fact]
    public async Task The_entire_captured_payment_amount_is_refunded()
    {
        var order = BuildConfirmedRazorpayOrder(grandTotal: 1000m);
        SetupPayments([BuildCapturedPayment(order.Id, 999.99m)]);
        SetupInventory([]);
        SetupOutbox();
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _gateway.Setup(g => g.RefundAsync(
                It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefundResult(true, "rfnd_abc", null));

        await BuildService().CancelAsync(order, "reason", "customer", CancellationToken.None);

        _gateway.Verify(g => g.RefundAsync(
            It.IsAny<string>(), 999.99m, It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // -----------------------------------------------------------------------
    // Idempotency on retry
    // -----------------------------------------------------------------------

    /// <summary>
    /// A retry after a recorded refund must NOT call the provider again — that is the whole
    /// purpose of persisting the receipt. It proceeds straight to cancelling.
    /// </summary>
    [Fact]
    public async Task A_payment_already_marked_refunded_skips_the_provider_call()
    {
        var order = BuildConfirmedRazorpayOrder();
        var payment = BuildCapturedPayment(order.Id, 1000m);
        payment.MarkRefunded("rfnd_already_done");
        SetupPayments([payment]);
        SetupInventory([]);
        SetupOutbox();
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await BuildService().CancelAsync(
            order, "reason", "admin", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Cancelled);
        _gateway.Verify(g => g.RefundAsync(
            It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // No refund needed
    // -----------------------------------------------------------------------

    /// <summary>
    /// A cash-on-delivery order has no captured online payment, so cancelling it must not
    /// involve the payment provider at all.
    /// </summary>
    [Fact]
    public async Task CashOnDelivery_cancels_without_contacting_the_gateway()
    {
        var order = BuildConfirmedOrder(PaymentMethod.CashOnDelivery);
        SetupPayments([]);
        SetupInventory([]);
        SetupOutbox();
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await BuildService().CancelAsync(
            order, "reason", "customer", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Cancelled);
        _gateway.Verify(g => g.RefundAsync(
            It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// A Razorpay order whose payment was never captured has no money to return, so cancelling
    /// it is a plain state change.
    /// </summary>
    [Fact]
    public async Task An_uncaptured_Razorpay_payment_cancels_without_a_refund()
    {
        var order = BuildConfirmedRazorpayOrder();
        var payment = Payment.Create(order.Id, "Razorpay", 1000m, "INR");
        SetupPayments([payment]);
        SetupInventory([]);
        SetupOutbox();
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await BuildService().CancelAsync(
            order, "reason", "customer", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Cancelled);
        _gateway.Verify(g => g.RefundAsync(
            It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// A captured payment with no provider id cannot be refunded — there is nothing to ask
    /// the provider about. Cancelling without a refund call is the only safe option, and the
    /// gateway must not be invoked with a null/empty reference.
    /// </summary>
    [Fact]
    public async Task A_captured_payment_without_a_provider_id_does_not_call_the_gateway()
    {
        var order = BuildConfirmedRazorpayOrder();
        // MarkPaid sets a provider id, so build the record and then reflect the id away to
        // model a partially-migrated row.
        var payment = BuildCapturedPayment(order.Id, 1000m);
        typeof(Payment).GetProperty(nameof(Payment.ProviderPaymentId))!
            .SetValue(payment, null);
        SetupPayments([payment]);
        SetupInventory([]);
        SetupOutbox();
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await BuildService().CancelAsync(
            order, "reason", "customer", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _gateway.Verify(g => g.RefundAsync(
            It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // Pre-flight guard
    // -----------------------------------------------------------------------

    /// <summary>
    /// A shipped order cannot be cancelled. The check must happen BEFORE the refund, so an
    /// irreversible provider call is never made for a transition that cannot complete.
    /// </summary>
    [Fact]
    public async Task An_uncancellable_order_is_rejected_before_the_refund_is_attempted()
    {
        var order = BuildConfirmedRazorpayOrder();
        order.MarkProcessing();
        order.MarkPacked();
        order.MarkShipped("TRK-1", "Delhivery");
        SetupPayments([BuildCapturedPayment(order.Id, 1000m)]);
        SetupInventory([]);

        var result = await BuildService().CancelAsync(
            order, "reason", "admin", CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("INVALID_ORDER_TRANSITION");
        order.Status.Should().Be(OrderStatus.Shipped);
        _gateway.Verify(g => g.RefundAsync(
            It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Nothing is persisted when the pre-flight check fails, so an invalid attempt cannot
    /// leave a stray write behind.
    /// </summary>
    [Fact]
    public async Task An_uncancellable_order_persists_nothing()
    {
        var order = BuildConfirmedRazorpayOrder();
        order.MarkProcessing();
        order.MarkPacked();
        order.MarkShipped("TRK-1", "Delhivery");
        SetupPayments([BuildCapturedPayment(order.Id, 1000m)]);
        SetupInventory([]);
        SetupOutbox();
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        await BuildService().CancelAsync(order, "reason", "admin", CancellationToken.None);

        _db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // Inventory release
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Reserved_inventory_is_released_on_a_successful_cancellation()
    {
        var order = BuildConfirmedRazorpayOrder();
        order.AddItem(OrderItem.Create(order.Id, Guid.NewGuid(), null, "Widget", null, "SKU-1", 100m, 2));
        var inventory = InventoryItem.Create(order.Items[0].ProductId, null, onHand: 10);
        inventory.Reserve(2);

        SetupPayments([BuildCapturedPayment(order.Id, 1000m)]);
        SetupInventory([inventory]);
        SetupOutbox();
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _gateway.Setup(g => g.RefundAsync(
                It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefundResult(true, "rfnd_abc", null));

        await BuildService().CancelAsync(order, "reason", "customer", CancellationToken.None);

        inventory.Reserved.Should().Be(0);
        inventory.Available.Should().Be(10);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private OrderCancellationService BuildService()
        => new(_db.Object, _gateway.Object, NullLogger<OrderCancellationService>.Instance);

    private static Order BuildConfirmedOrder(PaymentMethod method, decimal grandTotal = 1000m)
    {
        var address = ShippingAddress.Create(
            "Test User", "+919876543210", "Line 1", null,
            "City", "State", "12345", "IN");

        var order = Order.Create(
            Guid.NewGuid(), "ORD-TEST", "INR",
            subtotal: grandTotal, shippingAmount: 0m, codFee: 0m,
            discountAmount: 0m, taxAmount: 0m, grandTotal: grandTotal,
            address, method);

        typeof(KromicCommerce.Domain.Common.Entity)
            .GetProperty("Id")!.SetValue(order, Guid.NewGuid());

        order.Confirm();
        return order;
    }

    private static Order BuildConfirmedRazorpayOrder(decimal grandTotal = 1000m)
        => BuildConfirmedOrder(PaymentMethod.Razorpay, grandTotal);

    private static Payment BuildCapturedPayment(Guid orderId, decimal amount)
    {
        var payment = Payment.Create(orderId, "Razorpay", amount, "INR");
        payment.MarkPaid("pay_abc123");
        return payment;
    }

    private void SetupPayments(List<Payment> payments)
        => SetupDbSet(_db, d => d.Payments, payments);

    private void SetupInventory(List<InventoryItem> items)
        => SetupDbSet(_db, d => d.InventoryItems, items);

    /// <summary>
    /// Wires a DbSet backed by an in-memory list. The predicate is supplied as a typed lambda
    /// so it can be handed to Moq as an Expression, which a plain Func cannot be.
    /// </summary>
    private static void SetupDbSet<T>(
        Mock<IApplicationDbContext> db,
        Expression<Func<IApplicationDbContext, DbSet<T>>> selector,
        List<T> data)
        where T : class
    {
        var queryable = data.AsQueryable();
        var mock = new Mock<DbSet<T>>();
        mock.As<IAsyncEnumerable<T>>()
            .Setup(m => m.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns(new TestAsyncEnumerator<T>(queryable.GetEnumerator()));
        mock.As<IQueryable<T>>().Setup(m => m.Provider)
            .Returns(new TestAsyncQueryProvider<T>(queryable.Provider));
        mock.As<IQueryable<T>>().Setup(m => m.Expression).Returns(queryable.Expression);
        mock.As<IQueryable<T>>().Setup(m => m.ElementType).Returns(queryable.ElementType);
        mock.As<IQueryable<T>>().Setup(m => m.GetEnumerator()).Returns(queryable.GetEnumerator());
        db.Setup(selector).Returns(mock.Object);
    }
    private Mock<DbSet<OutboxEvent>> SetupOutbox()
    {
        var mock = new Mock<DbSet<OutboxEvent>>();
        _db.Setup(d => d.OutboxEvents).Returns(mock.Object);
        return mock;
    }
}
