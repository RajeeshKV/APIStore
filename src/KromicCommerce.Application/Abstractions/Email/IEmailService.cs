namespace KromicCommerce.Application.Abstractions.Email;

/// <summary>
/// Provider-independent email abstraction.
/// Supports both KromicManaged and CustomerBrevo modes via the implementation.
/// Application code never references Brevo SDK types directly.
/// Email sending is non-blocking for the core order transaction — failures do
/// NOT abort checkout. Failed emails are retried via the Outbox pattern.
/// Never log email credentials, API keys, or message bodies containing secrets.
/// </summary>
public interface IEmailService
{
    /// <summary>Sends an order confirmation email to the customer.</summary>
    Task SendOrderConfirmationAsync(OrderEmailContext ctx, CancellationToken cancellationToken = default);

    /// <summary>Sends a payment confirmed/paid email.</summary>
    Task SendPaymentConfirmationAsync(OrderEmailContext ctx, CancellationToken cancellationToken = default);

    /// <summary>Sends a payment failed notification.</summary>
    Task SendPaymentFailedAsync(OrderEmailContext ctx, string? reason, CancellationToken cancellationToken = default);

    /// <summary>Sends an order shipped notification with optional tracking info.</summary>
    Task SendOrderShippedAsync(OrderEmailContext ctx, string? trackingNumber, string? provider, CancellationToken cancellationToken = default);

    /// <summary>Sends an order cancellation notification to the customer.</summary>
    Task SendOrderCancelledAsync(OrderEmailContext ctx, string? reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a password reset email containing a one-time reset link.
    /// Never log the resetToken.
    /// </summary>
    Task SendPasswordResetAsync(string toEmail, string recipientName, string resetToken, CancellationToken cancellationToken = default);
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
    string? WebsiteUrl);
