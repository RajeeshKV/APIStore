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
    // Support desk
    //
    // The administrative notification recipient is resolved from configuration by the
    // dispatcher, not from a request. No customer-supplied value ever reaches ToAddress.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Tells an administrator that a ticket was opened or reopened. Both cases are one
    /// method because the resulting inbox item is the same: "this needs a response".
    /// </summary>
    Task SendTicketAdminNotificationAsync(TicketAdminNotificationContext ctx, CancellationToken ct = default);

    /// <summary>Tells a customer that their ticket changed state or received a reply.</summary>
    Task SendTicketStatusEmailAsync(TicketCustomerNotificationContext ctx, CancellationToken ct = default);

    /// <summary>
    /// Mails a rendered invoice as an attachment. Controlled by the merchant's global
    /// "automated invoice mailing" toggle, which the dispatcher checks before calling this.
    /// </summary>
    Task SendInvoiceEmailAsync(InvoiceMailContext ctx, CancellationToken ct = default);

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

// ---------------------------------------------------------------------------
// Support desk contexts
// ---------------------------------------------------------------------------

/// <summary>Why an administrator is being emailed. Drives the subject line and the call to action.</summary>
public enum TicketAdminAlertKind
{
    /// <summary>A brand new ticket.</summary>
    Created = 0,

    /// <summary>A previously resolved or closed ticket is back in the queue.</summary>
    Reopened = 1,

    /// <summary>The customer added a message to a ticket that is still open.</summary>
    CustomerReplied = 2
}

/// <summary>What a customer is being told happened to their ticket.</summary>
public enum TicketCustomerNoticeKind
{
    /// <summary>An administrator replied.</summary>
    Replied = 0,

    /// <summary>An administrator marked the ticket resolved.</summary>
    Resolved = 1,

    /// <summary>The ticket was closed.</summary>
    Closed = 2
}

/// <summary>
/// Administrative alert. Carries a deep link so the admin lands directly in the conversation
/// rather than having to search for the reference number.
/// </summary>
public sealed record TicketAdminNotificationContext(
    string AdminEmail,
    string AdminName,
    TicketAdminAlertKind Kind,
    string TicketId,
    string TicketNumber,
    string Subject,
    string CustomerName,
    string CustomerEmail,
    string? Description,
    string Status,
    int ReopenCount,
    string? OrderNumber,
    DateTime OccurredAtUtc,
    string? AdminTicketUrl,
    string BusinessName = "Store",
    string? LogoUrl = null);

/// <summary>Status or reply notification addressed to the customer who opened the ticket.</summary>
public sealed record TicketCustomerNotificationContext(
    string CustomerEmail,
    string CustomerName,
    TicketCustomerNoticeKind Kind,
    string TicketId,
    string TicketNumber,
    string Subject,
    string Status,
    string? AdminName,
    string? Note,
    string? AttachmentUrl,
    DateTime OccurredAtUtc,
    string? FrontendTicketUrl,
    string BusinessName = "Store",
    string? LogoUrl = null,
    string? SupportEmail = null);

/// <summary>
/// Everything needed to mail an invoice document. The subject is composed by the dispatcher
/// rather than here so that the merchant's override and the default wording live in one place.
/// </summary>
public sealed record InvoiceMailContext(
    string CustomerEmail,
    string CustomerName,
    string InvoiceNumber,
    string TicketNumber,
    string? OrderNumber,
    string CurrencyCode,
    decimal GrandTotal,
    string Subject,
    string HtmlBody,
    byte[] PdfBytes,
    string FileName,
    string BusinessName,
    string? LogoUrl = null,
    string? SupportEmail = null,
    string? WebsiteUrl = null);
