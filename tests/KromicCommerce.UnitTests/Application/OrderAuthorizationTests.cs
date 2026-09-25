using KromicCommerce.Application.Features.Orders.CancelOrder;
using KromicCommerce.Application.Features.Orders.GetMyOrders;
using KromicCommerce.Domain.Orders;
using Microsoft.Extensions.Logging.Abstractions;

namespace KromicCommerce.UnitTests.Application;

/// <summary>
/// Verifies customer isolation: customers can only access their own orders.
/// A query for another customer's order must return NotFound.
/// </summary>
public sealed class OrderAuthorizationTests
{
    private readonly Mock<IApplicationDbContext> _db = new();

    // -----------------------------------------------------------------------
    // GetMyOrderById — must be customer-scoped
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetMyOrderById_returns_not_found_for_different_customer()
    {
        var ownerId = Guid.NewGuid();
        var attackerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var order = BuildOrder(orderId, ownerId);
        SetupOrdersQueryable([order]);

        var handler = new GetMyOrderByIdHandler(_db.Object);
        var result = await handler.Handle(
            new GetMyOrderByIdQuery(orderId, attackerId), // attacker uses own ID
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task GetMyOrderById_returns_order_for_correct_customer()
    {
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var order = BuildOrder(orderId, customerId);
        order.AddItem(BuildOrderItem(orderId));
        SetupOrdersQueryable([order]);

        var handler = new GetMyOrderByIdHandler(_db.Object);
        var result = await handler.Handle(
            new GetMyOrderByIdQuery(orderId, customerId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // CancelOrder — must be customer-scoped
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CancelOrder_returns_not_found_for_different_customer()
    {
        var ownerId = Guid.NewGuid();
        var attackerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var order = BuildOrder(orderId, ownerId);
        SetupOrdersQueryable([order]);
        SetupOrderItems([]);

        var handler = new CancelOrderHandler(_db.Object, NullLogger<CancelOrderHandler>.Instance);
        var result = await handler.Handle(
            new CancelOrderCommand(orderId, attackerId, null), // attacker
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Order BuildOrder(Guid orderId, Guid customerId)
    {
        var address = ShippingAddress.Create(
            "Test User", "+919876543210", "Line 1", null,
            "City", "State", "12345", "IN");
        var order = Order.Create(customerId, "ORD-TEST", "INR",
            100m, 0m, 0m, 0m, 0m, 100m, address, PaymentMethod.Razorpay);
        typeof(KromicCommerce.Domain.Common.Entity).GetProperty("Id")!.SetValue(order, orderId);
        return order;
    }

    private static OrderItem BuildOrderItem(Guid orderId)
        => OrderItem.Create(orderId, Guid.NewGuid(), null, "Product", null, null, 100m, 1);

    private void SetupOrdersQueryable(List<Order> orders)
    {
        var data = orders.AsQueryable();
        var mock = new Mock<Microsoft.EntityFrameworkCore.DbSet<Order>>();
        mock.As<IAsyncEnumerable<Order>>()
            .Setup(m => m.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns(new TestAsyncEnumerator<Order>(data.GetEnumerator()));
        mock.As<IQueryable<Order>>().Setup(m => m.Provider)
            .Returns(new TestAsyncQueryProvider<Order>(data.Provider));
        mock.As<IQueryable<Order>>().Setup(m => m.Expression).Returns(data.Expression);
        mock.As<IQueryable<Order>>().Setup(m => m.ElementType).Returns(data.ElementType);
        mock.As<IQueryable<Order>>().Setup(m => m.GetEnumerator()).Returns(data.GetEnumerator());
        _db.Setup(d => d.Orders).Returns(mock.Object);
    }

    private void SetupOrderItems(List<OrderItem> items)
    {
        var data = items.AsQueryable();
        var mock = new Mock<Microsoft.EntityFrameworkCore.DbSet<OrderItem>>();
        mock.As<IAsyncEnumerable<OrderItem>>()
            .Setup(m => m.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns(new TestAsyncEnumerator<OrderItem>(data.GetEnumerator()));
        mock.As<IQueryable<OrderItem>>().Setup(m => m.Provider)
            .Returns(new TestAsyncQueryProvider<OrderItem>(data.Provider));
        mock.As<IQueryable<OrderItem>>().Setup(m => m.Expression).Returns(data.Expression);
        mock.As<IQueryable<OrderItem>>().Setup(m => m.ElementType).Returns(data.ElementType);
        mock.As<IQueryable<OrderItem>>().Setup(m => m.GetEnumerator()).Returns(data.GetEnumerator());
        _db.Setup(d => d.OrderItems).Returns(mock.Object);
    }
}
