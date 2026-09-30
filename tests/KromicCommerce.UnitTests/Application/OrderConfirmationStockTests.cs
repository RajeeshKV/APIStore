using System.Linq.Expressions;
using KromicCommerce.Application.Abstractions.Payments;
using KromicCommerce.Application.Abstractions.Catalog;
using KromicCommerce.Application.Services;
using KromicCommerce.Domain.Catalog;
using KromicCommerce.Application.Features.Orders;
using KromicCommerce.Application.Features.Orders.Admin;
using KromicCommerce.Contracts.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace KromicCommerce.UnitTests.Application;

/// <summary>
/// Stock consumption at order confirmation.
///
/// Admin confirmation is the single consumption point for BOTH payment methods: COD sits at
/// OrderPlaced until an admin confirms it, and Razorpay moves PendingPayment → OrderPlaced on
/// verified payment and is likewise confirmed by an admin. Both therefore reserve at checkout
/// and finalise here, so the two methods cannot drift apart.
///
/// The behaviour under test is that consumption is all-or-nothing. A partially finalised order
/// is unrepairable: the order is already Confirmed, so re-confirming throws and the shortfall
/// is permanent.
/// </summary>
public sealed class OrderConfirmationStockTests
{
    private readonly Mock<IApplicationDbContext> _db = new();
    private readonly Mock<IPaymentGateway> _gateway = new();
    private readonly Mock<ICatalogCacheService> _cache = new();

    // -----------------------------------------------------------------------
    // Consumption
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Confirming_an_order_consumes_its_reserved_stock()
    {
        var productId = Guid.NewGuid();
        var order = BuildOrderPlaced((productId, null, 3));
        var inventory = InventoryItem.Create(productId, null, onHand: 10);
        inventory.Reserve(3);              // checkout reserved the units

        var handler = BuildHandler();
        SetupOrder(order, inventory);

        var result = await handler.Handle(
            new UpdateOrderStatusCommand(order.Id, OrderStatus.Confirmed, null, null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Confirmed);

        // Reserved units become sold: removed from both buckets.
        inventory.OnHand.Should().Be(7);
        inventory.Reserved.Should().Be(0);
        inventory.Available.Should().Be(7);
    }

    [Fact]
    public async Task Confirming_sells_the_last_unit_and_flips_the_product_out_of_stock()
    {
        var productId = Guid.NewGuid();
        var order = BuildOrderPlaced((productId, null, 1));
        var inventory = InventoryItem.Create(productId, null, onHand: 1);
        inventory.IsOutOfStock.Should().BeFalse("one unit is sellable before it is reserved");

        inventory.Reserve(1);
        inventory.IsOutOfStock.Should().BeTrue("the last unit is now held for the order");

        SetupOrder(order, inventory);

        var result = await BuildHandler().Handle(
            new UpdateOrderStatusCommand(order.Id, OrderStatus.Confirmed, null, null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        inventory.Available.Should().Be(0);
        inventory.IsOutOfStock.Should().BeTrue();
    }

    /// <summary>
    /// A variant order must deduct the variant's own inventory row, never the base product row.
    /// </summary>
    [Fact]
    public async Task A_variant_order_deducts_the_variant_row_not_the_base_row()
    {
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var order = BuildOrderPlaced((productId, variantId, 2));

        var baseStock = InventoryItem.Create(productId, null, onHand: 50);
        var variantStock = InventoryItem.Create(productId, variantId, onHand: 4);
        variantStock.Reserve(2);

        SetupOrder(order, baseStock, variantStock);

        var result = await BuildHandler().Handle(
            new UpdateOrderStatusCommand(order.Id, OrderStatus.Confirmed, null, null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        variantStock.OnHand.Should().Be(2);
        baseStock.OnHand.Should().Be(50, "the base row must not be touched by a variant order");
    }

    [Fact]
    public async Task Multiple_lines_deduct_from_their_own_rows()
    {
        var productA = Guid.NewGuid();
        var productB = Guid.NewGuid();
        var order = BuildOrderPlaced((productA, null, 2), (productB, null, 1));

        var stockA = InventoryItem.Create(productA, null, onHand: 10);
        var stockB = InventoryItem.Create(productB, null, onHand: 5);
        stockA.Reserve(2);
        stockB.Reserve(1);

        SetupOrder(order, stockA, stockB);

        var result = await BuildHandler().Handle(
            new UpdateOrderStatusCommand(order.Id, OrderStatus.Confirmed, null, null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        stockA.OnHand.Should().Be(8);
        stockB.OnHand.Should().Be(4);
    }

    // -----------------------------------------------------------------------
    // Idempotency
    // -----------------------------------------------------------------------

    /// <summary>
    /// The state machine refuses a second Confirmed → Confirmed transition, so a retried
    /// confirmation event cannot decrement stock twice. This is the guarantee that makes
    /// order status a sufficient idempotency record for consumption.
    /// </summary>
    [Fact]
    public async Task Confirming_twice_is_refused_and_does_not_double_consume()
    {
        var productId = Guid.NewGuid();
        var order = BuildOrderPlaced((productId, null, 2));
        var inventory = InventoryItem.Create(productId, null, onHand: 10);
        inventory.Reserve(2);

        var handler = BuildHandler();
        SetupOrder(order, inventory);

        await handler.Handle(
            new UpdateOrderStatusCommand(order.Id, OrderStatus.Confirmed, null, null, null),
            CancellationToken.None);
        inventory.OnHand.Should().Be(8);

        var second = await handler.Handle(
            new UpdateOrderStatusCommand(order.Id, OrderStatus.Confirmed, null, null, null),
            CancellationToken.None);

        second.IsFailure.Should().BeTrue();
        second.Error.Code.Should().Be("INVALID_ORDER_TRANSITION");
        inventory.OnHand.Should().Be(8, "a retried confirmation must not decrement again");
    }

    // -----------------------------------------------------------------------
    // All-or-nothing consumption
    // -----------------------------------------------------------------------

    /// <summary>
    /// One line cannot be finalised because its reservation is short (the reservation was
    /// released, e.g. by an earlier failed payment). Confirmation must be abandoned entirely:
    /// the order stays unconfirmed and the good line keeps its reservation, so the admin can
    /// retry once the shortfall is resolved.
    ///
    /// Previously the failure was logged and swallowed, so the order was Confirmed with only
    /// some of its stock consumed and no way to ever repair it.
    /// </summary>
    [Fact]
    public async Task A_line_that_cannot_be_finalised_prevents_confirmation_entirely()
    {
        var goodProduct = Guid.NewGuid();
        var badProduct = Guid.NewGuid();
        var order = BuildOrderPlaced((goodProduct, null, 1), (badProduct, null, 5));

        var goodStock = InventoryItem.Create(goodProduct, null, onHand: 10);
        goodStock.Reserve(1);

        // Only 2 of the 5 required units are reserved — finalising will throw.
        var badStock = InventoryItem.Create(badProduct, null, onHand: 10);
        badStock.Reserve(2);

        SetupOrder(order, goodStock, badStock);

        var result = await BuildHandler().Handle(
            new UpdateOrderStatusCommand(order.Id, OrderStatus.Confirmed, null, null, null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("INVALID_ORDER_TRANSITION");

        // Neither the order nor any stock may have moved.
        order.Status.Should().Be(OrderStatus.OrderPlaced);
        goodStock.OnHand.Should().Be(10);
        goodStock.Reserved.Should().Be(1, "the good line must keep its reservation for a retry");
        badStock.OnHand.Should().Be(10);
    }

    /// <summary>
    /// Nothing is persisted when consumption fails, so the failed attempt cannot leave a
    /// partial write or a stray event behind.
    /// </summary>
    [Fact]
    public async Task A_failed_confirmation_persists_nothing()
    {
        var productId = Guid.NewGuid();
        var order = BuildOrderPlaced((productId, null, 5));
        var inventory = InventoryItem.Create(productId, null, onHand: 10);
        inventory.Reserve(2);   // short

        var mockOutbox = new Mock<DbSet<OutboxEvent>>();
        _db.Setup(d => d.OutboxEvents).Returns(mockOutbox.Object);
        SetupOrder(order, inventory);
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await BuildHandler().Handle(
            new UpdateOrderStatusCommand(order.Id, OrderStatus.Confirmed, null, null, null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        _db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        mockOutbox.Verify(o => o.Add(It.IsAny<OutboxEvent>()), Times.Never);
    }

    /// <summary>
    /// A product with no inventory row has nothing to consume. This is a legitimate state for
    /// products created before stock tracking, so it must not block confirmation.
    /// </summary>
    [Fact]
    public async Task A_product_with_no_inventory_row_does_not_block_confirmation()
    {
        var order = BuildOrderPlaced((Guid.NewGuid(), null, 2));
        SetupOrder(order);   // no inventory rows at all

        var result = await BuildHandler().Handle(
            new UpdateOrderStatusCommand(order.Id, OrderStatus.Confirmed, null, null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Confirmed);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private UpdateOrderStatusHandler BuildHandler()
        => new(_db.Object,
               new OrderCancellationService(_db.Object, _gateway.Object,
                   new OrderInventoryRestorer(_db.Object, _cache.Object,
                       NullLogger<OrderInventoryRestorer>.Instance),
                   NullLogger<OrderCancellationService>.Instance),
               new OrderInventoryRestorer(_db.Object, _cache.Object,
                   NullLogger<OrderInventoryRestorer>.Instance),
               NullLogger<UpdateOrderStatusHandler>.Instance);

    /// <summary>
    /// Builds an order sitting at OrderPlaced — the state both COD and Razorpay orders are in
    /// when awaiting merchant confirmation.
    /// </summary>
    private static Order BuildOrderPlaced(params (Guid ProductId, Guid? VariantId, int Qty)[] lines)
    {
        var address = ShippingAddress.Create(
            "Test User", "+919876543210", "Line 1", null, "City", "State", "12345", "IN");

        var order = Order.Create(
            Guid.NewGuid(), "ORD-TEST", "INR",
            subtotal: 1000m, shippingAmount: 0m, codFee: 0m,
            discountAmount: 0m, taxAmount: 0m, grandTotal: 1000m,
            address, PaymentMethod.Razorpay);

        typeof(KromicCommerce.Domain.Common.Entity)
            .GetProperty("Id")!.SetValue(order, Guid.NewGuid());

        foreach (var (productId, variantId, qty) in lines)
        {
            var item = OrderItem.Create(
                order.Id, productId, variantId, "Widget", null, "SKU-1", 100m, qty);

            // Checkout records the reservation on each line. Confirmation now consumes exactly
            // this recorded quantity, so the fixture must reproduce what checkout persists —
            // otherwise the lines stay Untracked and confirmation legitimately skips them.
            item.MarkInventoryReserved(qty);

            order.AddItem(item);
        }

        return order;
    }

    private void SetupOrder(Order order, params InventoryItem[] inventory)
    {
        var orderMock = BuildDbSet(new List<Order> { order });
        _db.Setup(d => d.Orders).Returns(orderMock.Object);

        var invMock = BuildDbSet(inventory.ToList());
        _db.Setup(d => d.InventoryItems).Returns(invMock.Object);

        // Confirming stock evicts the cached availability projections, which resolves each
        // affected product's slug. Wire Products so that lookup runs against a real (empty)
        // queryable rather than an unconfigured mock that yields null and throws.
        _db.Setup(d => d.Products).Returns(BuildDbSet(new List<Product>()).Object);

        // The handler builds its response after the transition, which resolves the display
        // image for each line. Empty is fine — no line has a product image.
        _db.Setup(d => d.ProductImages).Returns(BuildDbSet(new List<ProductImage>()).Object);

        _db.Setup(d => d.OutboxEvents).Returns(new Mock<DbSet<OutboxEvent>>().Object);
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private static Mock<DbSet<T>> BuildDbSet<T>(List<T> data) where T : class
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
        return mock;
    }
}
