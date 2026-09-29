using KromicCommerce.Domain.Orders.Events;

namespace KromicCommerce.Domain.Orders;

/// <summary>
/// Order aggregate root.
///
/// Historical snapshot rule: all prices, product names, and variant details are stored
/// on the order/order items at creation time. The order history must remain correct
/// even if catalog data changes, products are archived, or prices are updated.
///
/// State machine: transitions are enforced by domain methods. Invalid transitions
/// throw domain errors rather than silently succeeding.
///
/// Money: all amounts are decimal. Never use double/float for monetary calculations.
/// </summary>
public sealed class Order : AuditableEntity
{
    private Order() { } // EF constructor

    public static Order Create(
        Guid customerId,
        string orderNumber,
        string currencyCode,
        decimal subtotal,
        decimal shippingAmount,
        decimal codFee,
        decimal discountAmount,
        decimal taxAmount,
        decimal grandTotal,
        ShippingAddress shippingAddress,
        PaymentMethod paymentMethod,
        string? appliedCouponCode = null)
    {
        var order = new Order
        {
            CustomerId = customerId,
            OrderNumber = orderNumber,
            CurrencyCode = currencyCode,
            Subtotal = subtotal,
            ShippingAmount = shippingAmount,
            CodFee = codFee,
            DiscountAmount = discountAmount,
            TaxAmount = taxAmount,
            GrandTotal = grandTotal,
            ShippingAddress = shippingAddress,
            PaymentMethod = paymentMethod,
            AppliedCouponCode = appliedCouponCode,
            Status = OrderStatus.OrderPlaced,
            OrderPlacedAt = DateTime.UtcNow
        };
        order.RaiseDomainEvent(new OrderCreatedEvent(order.Id, customerId, grandTotal, currencyCode));
        return order;
    }

    // -----------------------------------------------------------------------
    // Core fields
    // -----------------------------------------------------------------------
    public Guid CustomerId { get; private set; }

    /// <summary>Human-readable order number (e.g. ORD-20261001-0001). Unique per deployment.</summary>
    public string OrderNumber { get; private set; } = string.Empty;

    public OrderStatus Status { get; private set; }
    public PaymentMethod PaymentMethod { get; private set; }

    // -----------------------------------------------------------------------
    // Money (decimal, never double/float)
    // -----------------------------------------------------------------------
    public string CurrencyCode { get; private set; } = "INR";
    public decimal Subtotal { get; private set; }
    public decimal ShippingAmount { get; private set; }
    public decimal CodFee { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal GrandTotal { get; private set; }

    /// <summary>Coupon code applied at checkout. Null when no promotion was used.</summary>
    public string? AppliedCouponCode { get; private set; }

    // -----------------------------------------------------------------------
    // Address snapshot — historically stable
    // -----------------------------------------------------------------------
    public ShippingAddress ShippingAddress { get; private set; } = null!;

    // -----------------------------------------------------------------------
    // Tracking
    // -----------------------------------------------------------------------
    public string? TrackingNumber { get; private set; }
    public string? TrackingProvider { get; private set; }
    public string? CancellationReason { get; private set; }
    public DateTime? OrderPlacedAt { get; private set; }
    public DateTime? PaidAt { get; private set; }
    public DateTime? ShippedAt { get; private set; }
    public DateTime? DeliveredAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }

    // Navigation
    private readonly List<OrderItem> _items = [];
    public IReadOnlyList<OrderItem> Items => _items.AsReadOnly();

    // -----------------------------------------------------------------------
    // State machine
    // -----------------------------------------------------------------------

    private static readonly Dictionary<OrderStatus, HashSet<OrderStatus>> AllowedTransitions = new()
    {
        // New orders always start at OrderPlaced
        [OrderStatus.OrderPlaced]       = [OrderStatus.PendingPayment, OrderStatus.Confirmed, OrderStatus.Cancelled, OrderStatus.Failed],
        // Razorpay: OrderPlaced → PendingPayment while awaiting payment widget
        [OrderStatus.PendingPayment]    = [OrderStatus.PaymentProcessing, OrderStatus.OrderPlaced, OrderStatus.Cancelled, OrderStatus.Failed],
        [OrderStatus.PaymentProcessing] = [OrderStatus.Confirmed, OrderStatus.Failed, OrderStatus.Cancelled],
        [OrderStatus.Confirmed]         = [OrderStatus.Processing, OrderStatus.Cancelled],
        [OrderStatus.Processing]        = [OrderStatus.Packed, OrderStatus.Cancelled],
        [OrderStatus.Packed]            = [OrderStatus.Shipped, OrderStatus.Cancelled],
        [OrderStatus.Shipped]           = [OrderStatus.Delivered],
        [OrderStatus.Delivered]         = [OrderStatus.RefundPending],
        [OrderStatus.RefundPending]     = [OrderStatus.Refunded],
        [OrderStatus.Cancelled]         = [],
        [OrderStatus.Failed]            = [],
        [OrderStatus.Refunded]          = []
    };

    private void Transition(OrderStatus newStatus, string? reason = null)
    {
        if (!AllowedTransitions.TryGetValue(Status, out var allowed) || !allowed.Contains(newStatus))
            throw new InvalidOperationException(
                $"Order cannot transition from {Status} to {newStatus}.");

        Status = newStatus;
        RaiseDomainEvent(new OrderStatusChangedEvent(Id, Status));
        if (reason is not null) CancellationReason = reason;
    }

    /// <summary>
    /// Marks the order as awaiting Razorpay payment.
    /// Called after Razorpay payment widget is initialized.
    /// </summary>
    public void MarkPendingPayment()       => Transition(OrderStatus.PendingPayment);

    public void MarkPaymentProcessing()    => Transition(OrderStatus.PaymentProcessing);

    /// <summary>
    /// Merchant confirms the order is ready for fulfillment.
    /// Must be called explicitly by admin after verifying stock.
    /// </summary>
    public void Confirm()                  => Transition(OrderStatus.Confirmed);

    public void MarkProcessing()           => Transition(OrderStatus.Processing);
    public void MarkPacked()               => Transition(OrderStatus.Packed);
    public void MarkShipped(string? trackingNumber = null, string? provider = null)
    {
        Transition(OrderStatus.Shipped);
        TrackingNumber = trackingNumber;
        TrackingProvider = provider;
        ShippedAt = DateTime.UtcNow;
    }
    public void MarkDelivered()            { Transition(OrderStatus.Delivered); DeliveredAt = DateTime.UtcNow; }
    public void MarkFailed()               => Transition(OrderStatus.Failed);
    public void Cancel(string? reason = null)
    {
        Transition(OrderStatus.Cancelled, reason);
        CancelledAt = DateTime.UtcNow;
    }
    public void MarkRefundPending()        => Transition(OrderStatus.RefundPending);
    public void MarkRefunded()             => Transition(OrderStatus.Refunded);

    /// <summary>
    /// Records when payment is captured/confirmed.
    /// Separate from Confirm() — paying for an order ≠ merchant confirming fulfillment.
    /// </summary>
    public void RecordPayment()            { PaidAt = DateTime.UtcNow; }

    /// <summary>
    /// Called after successful Razorpay payment verification.
    /// Transitions PendingPayment → OrderPlaced (paid, awaiting merchant confirmation).
    /// Payment is received but merchant still needs to confirm fulfillment.
    /// </summary>
    public void MarkPaymentReceived()
    {
        Transition(OrderStatus.OrderPlaced);
        PaidAt = DateTime.UtcNow;
    }

    // -----------------------------------------------------------------------
    // Items
    // -----------------------------------------------------------------------

    public void AddItem(OrderItem item) => _items.Add(item);
}
