namespace KromicCommerce.Application.Abstractions.Email;

/// <summary>
/// Provider-independent email abstraction.
/// Email sending is non-blocking for the core order transaction — failures do
/// NOT abort checkout. Failed emails are retried via the Outbox pattern.
/// Never log email credentials, API keys, or message bodies containing secrets.
/// </summary>
public interface IEmailService
{
    // -----------------------------------------------------------------------
    // Order lifecycle emails — triggered via Outbox after status transitions
    // -----------------------------------------------------------------------

    /// <summary>Order Placed — customer submitted the order, awaiting merchant confirmation.</summary>
    Task SendOrderPlacedAsync(OrderEmailContext ctx, CancellationToken ct = default);

    /// <summary>Order Confirmed — merchant verified stock and accepted the order.</summary>
    Task SendOrderConfirmedAsync(OrderEmailContext ctx, CancellationToken ct = default);

    /// <summary>Payment confirmed/captured (Razorpay). Separate from order confirmation.</summary>
    Task SendPaymentConfirmationAsync(OrderEmailContext ctx, CancellationToken ct = default);

    /// <summary>Payment failed — customer should retry.</summary>
    Task SendPaymentFailedAsync(OrderEmailContext ctx, string? reason, CancellationToken ct = default);

    /// <summary>Order shipped — includes optional tracking number and provider.</summary>
    Task SendOrderShippedAsync(OrderEmailContext ctx, string? trackingNumber, string? provider, CancellationToken ct = default);

    /// <summary>Order delivered.</summary>
    Task SendOrderDeliveredAsync(OrderEmailContext ctx, CancellationToken ct = default);

    /// <summary>Order cancelled — includes optional reason.</summary>
    Task SendOrderCancelledAsync(OrderEmailContext ctx, string? reason, CancellationToken ct = default);

    /// <summary>Refund initiated.</summary>
    Task SendOrderRefundedAsync(OrderEmailContext ctx, CancellationToken ct = default);

    // -----------------------------------------------------------------------
    // Auth emails
    // -----------------------------------------------------------------------

    /// <summary>Password reset — resetToken must never be logged.</summary>
    Task SendPasswordResetAsync(string toEmail, string recipientName, string resetToken, CancellationToken ct = default);

    // -----------------------------------------------------------------------
    // Legacy — kept for backward compatibility with existing outbox processor
    // -----------------------------------------------------------------------

    /// <summary>Legacy order confirmation email (maps to SendOrderPlacedAsync).</summary>
    Task SendOrderConfirmationAsync(OrderEmailContext ctx, CancellationToken cancellationToken = default);
}

/// <summary>Minimal context required to build transactional order emails.</summary>
public sealed record OrderEmailContext(
    string CustomerEmail,
    string CustomerName,
    string OrderNumber,
    decimal GrandTotal,
    string Currency,
    string BusinessName,
    string? LogoUrl,
    string? SupportEmail,
    string? WebsiteUrl,
    string? FrontendUrl = null);
