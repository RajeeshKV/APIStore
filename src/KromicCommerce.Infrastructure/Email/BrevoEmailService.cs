using System.Net.Http.Json;
using KromicCommerce.Application.Abstractions.Email;
using KromicCommerce.Infrastructure.Configuration;

namespace KromicCommerce.Infrastructure.Email;

/// <summary>
/// Brevo (formerly Sendinblue) transactional email implementation.
/// All order email templates are code-managed — no Brevo dashboard templates needed.
/// API key is never logged.
/// </summary>
internal sealed class BrevoEmailService(
    IOptions<BrevoOptions> options,
    IHttpClientFactory httpClientFactory,
    ILogger<BrevoEmailService> logger) : IEmailService
{
    private const string ApiBaseUrl = "https://api.brevo.com/v3/smtp/email";

    // -----------------------------------------------------------------------
    // Order lifecycle emails
    // -----------------------------------------------------------------------

    public Task SendOrderPlacedAsync(OrderEmailContext ctx, CancellationToken ct = default)
    {
        var subject = $"Order #{ctx.OrderNumber} Placed Successfully";
        var body = $"""
            <h1 style="color:#1a1a1a;font-size:22px;margin:0 0 16px">Your order has been placed!</h1>
            <p style="color:#444;margin:0 0 12px">
              Hi {H(ctx.CustomerName)}, thank you for your order.
              We've received it and are reviewing item availability.
            </p>
            <div style="background:#f8f8f8;border-radius:6px;padding:16px;margin:20px 0">
              <p style="margin:0 0 6px"><strong>Order Number:</strong> {H(ctx.OrderNumber)}</p>
              <p style="margin:0 0 6px"><strong>Order Total:</strong> {ctx.GrandTotal:F2} {H(ctx.Currency)}</p>
              <p style="margin:0;color:#888;font-size:13px">
                We will notify you once your order is confirmed by our team.
              </p>
            </div>
            {ViewOrderButton(ctx)}
            <p style="color:#888;font-size:13px;margin-top:20px">
              If you did not place this order, please contact us immediately.
            </p>
            """;
        return SendAsync(ctx, subject, BuildEmail(ctx, body), ct);
    }

    public Task SendOrderConfirmedAsync(OrderEmailContext ctx, CancellationToken ct = default)
    {
        var subject = $"Order #{ctx.OrderNumber} Confirmed";
        var body = $"""
            <h1 style="color:#1a1a1a;font-size:22px;margin:0 0 16px">Your order is confirmed!</h1>
            <p style="color:#444;margin:0 0 12px">
              Hi {H(ctx.CustomerName)}, great news — we've verified your order
              and confirmed that all items are available.
            </p>
            <div style="background:#f0fdf4;border:1px solid #bbf7d0;border-radius:6px;padding:16px;margin:20px 0">
              <p style="margin:0 0 6px"><strong>Order Number:</strong> {H(ctx.OrderNumber)}</p>
              <p style="margin:0 0 6px"><strong>Order Total:</strong> {ctx.GrandTotal:F2} {H(ctx.Currency)}</p>
              <p style="margin:0;color:#166534;font-size:13px">
                We are now preparing your order for shipment.
              </p>
            </div>
            {ViewOrderButton(ctx)}
            """;
        return SendAsync(ctx, subject, BuildEmail(ctx, body), ct);
    }

    public Task SendPaymentConfirmationAsync(OrderEmailContext ctx, CancellationToken ct = default)
    {
        var subject = $"Payment Received — Order #{ctx.OrderNumber}";
        var body = $"""
            <h1 style="color:#1a1a1a;font-size:22px;margin:0 0 16px">Payment confirmed</h1>
            <p style="color:#444;margin:0 0 12px">
              Hi {H(ctx.CustomerName)}, we have successfully received your payment.
            </p>
            <div style="background:#f8f8f8;border-radius:6px;padding:16px;margin:20px 0">
              <p style="margin:0 0 6px"><strong>Order Number:</strong> {H(ctx.OrderNumber)}</p>
              <p style="margin:0"><strong>Amount Paid:</strong> {ctx.GrandTotal:F2} {H(ctx.Currency)}</p>
            </div>
            <p style="color:#444">
              Our team will review and confirm your order shortly.
            </p>
            {ViewOrderButton(ctx)}
            """;
        return SendAsync(ctx, subject, BuildEmail(ctx, body), ct);
    }

    public Task SendPaymentFailedAsync(OrderEmailContext ctx, string? reason, CancellationToken ct = default)
    {
        var subject = $"Payment Failed — Order #{ctx.OrderNumber}";
        var reasonHtml = reason is not null
            ? $"<p style='color:#888;font-size:13px'>Reason: {H(reason)}</p>"
            : "";
        var body = $"""
            <h1 style="color:#dc2626;font-size:22px;margin:0 0 16px">Payment unsuccessful</h1>
            <p style="color:#444;margin:0 0 12px">
              Hi {H(ctx.CustomerName)}, unfortunately your payment for order
              <strong>{H(ctx.OrderNumber)}</strong> could not be processed.
            </p>
            {reasonHtml}
            <p style="color:#444">Please try placing the order again or use a different payment method.</p>
            """;
        return SendAsync(ctx, subject, BuildEmail(ctx, body), ct);
    }

    public Task SendOrderShippedAsync(
        OrderEmailContext ctx, string? trackingNumber, string? provider, CancellationToken ct = default)
    {
        var subject = $"Order #{ctx.OrderNumber} Has Shipped";
        var trackingHtml = trackingNumber is not null
            ? $"""
              <div style="background:#eff6ff;border:1px solid #bfdbfe;border-radius:6px;padding:16px;margin:20px 0">
                <p style="margin:0 0 4px;font-weight:bold">Tracking Information</p>
                <p style="margin:0">Tracking Number: <strong>{H(trackingNumber)}</strong>
                  {(provider is not null ? $" via {H(provider)}" : "")}</p>
              </div>
              """
            : "<p style='color:#888'>Tracking information will be updated shortly.</p>";

        var body = $"""
            <h1 style="color:#1a1a1a;font-size:22px;margin:0 0 16px">Your order is on its way!</h1>
            <p style="color:#444;margin:0 0 12px">
              Hi {H(ctx.CustomerName)}, your order <strong>{H(ctx.OrderNumber)}</strong> has been shipped.
            </p>
            {trackingHtml}
            {ViewOrderButton(ctx)}
            """;
        return SendAsync(ctx, subject, BuildEmail(ctx, body), ct);
    }

    public Task SendOrderDeliveredAsync(OrderEmailContext ctx, CancellationToken ct = default)
    {
        var subject = $"Order #{ctx.OrderNumber} Delivered";
        var body = $"""
            <h1 style="color:#1a1a1a;font-size:22px;margin:0 0 16px">Order delivered!</h1>
            <p style="color:#444;margin:0 0 12px">
              Hi {H(ctx.CustomerName)}, your order <strong>{H(ctx.OrderNumber)}</strong>
              has been delivered. We hope you enjoy your purchase!
            </p>
            <p style="color:#444">If you have any questions or concerns, please don't hesitate to contact us.</p>
            {ViewOrderButton(ctx)}
            """;
        return SendAsync(ctx, subject, BuildEmail(ctx, body), ct);
    }

    public Task SendOrderCancelledAsync(OrderEmailContext ctx, string? reason, CancellationToken ct = default)
    {
        var subject = $"Order #{ctx.OrderNumber} Cancelled";
        var reasonHtml = reason is not null
            ? $"<p style='color:#888;font-size:13px;margin:8px 0 0'>Reason: {H(reason)}</p>"
            : "";
        var body = $"""
            <h1 style="color:#dc2626;font-size:22px;margin:0 0 16px">Order cancelled</h1>
            <p style="color:#444;margin:0 0 12px">
              Hi {H(ctx.CustomerName)}, your order <strong>{H(ctx.OrderNumber)}</strong> has been cancelled.
            </p>
            {reasonHtml}
            <p style="color:#444">If a payment was made, a refund will be processed to your original payment method.</p>
            <p style="color:#444">If you have any questions, please contact us.</p>
            """;
        return SendAsync(ctx, subject, BuildEmail(ctx, body), ct);
    }

    public Task SendOrderRefundedAsync(OrderEmailContext ctx, CancellationToken ct = default)
    {
        var subject = $"Refund Processed — Order #{ctx.OrderNumber}";
        var body = $"""
            <h1 style="color:#1a1a1a;font-size:22px;margin:0 0 16px">Refund processed</h1>
            <p style="color:#444;margin:0 0 12px">
              Hi {H(ctx.CustomerName)}, your refund of
              <strong>{ctx.GrandTotal:F2} {H(ctx.Currency)}</strong>
              for order <strong>{H(ctx.OrderNumber)}</strong> has been processed.
            </p>
            <p style="color:#888;font-size:13px">
              Refunds typically appear in your account within 5–7 business days
              depending on your bank.
            </p>
            """;
        return SendAsync(ctx, subject, BuildEmail(ctx, body), ct);
    }

    // -----------------------------------------------------------------------
    // Auth
    // -----------------------------------------------------------------------

    public async Task SendPasswordResetAsync(
        string toEmail, string recipientName, string resetToken, CancellationToken ct = default)
    {
        // resetToken is NEVER logged
        var subject = "Password Reset Request";
        var html = $"""
            <html><body style="font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;background:#f9f9f9;margin:0;padding:0">
              <div style="max-width:560px;margin:40px auto;background:#fff;border-radius:8px;overflow:hidden;box-shadow:0 1px 3px rgba(0,0,0,.08)">
                <div style="padding:32px">
                  <h2 style="color:#1a1a1a;margin:0 0 16px">Password Reset</h2>
                  <p style="color:#444;margin:0 0 12px">Hi {H(recipientName)},</p>
                  <p style="color:#444;margin:0 0 20px">
                    We received a request to reset your password.
                    Enter the code below in the password reset form. It expires in 15 minutes.
                  </p>
                  <div style="background:#f3f4f6;border-radius:6px;padding:16px;text-align:center;letter-spacing:4px;font-size:24px;font-weight:bold;color:#1a1a1a;margin:0 0 20px">
                    {H(resetToken)}
                  </div>
                  <p style="color:#888;font-size:13px">If you did not request this, you can safely ignore this email.</p>
                </div>
              </div>
            </body></html>
            """;
        await SendAsync(toEmail, recipientName, subject, html, ct);
    }

    // -----------------------------------------------------------------------
    // Legacy compatibility
    // -----------------------------------------------------------------------

    public Task SendOrderConfirmationAsync(OrderEmailContext ctx, CancellationToken cancellationToken = default)
        => SendOrderPlacedAsync(ctx, cancellationToken);

    // -----------------------------------------------------------------------
    // Template builder
    // -----------------------------------------------------------------------

    private string BuildEmail(OrderEmailContext ctx, string body)
    {
        var logo = ctx.LogoUrl is not null
            ? $"<img src='{ctx.LogoUrl}' alt='{H(ctx.BusinessName)}' style='max-height:44px;display:block'/>"
            : $"<span style='font-size:18px;font-weight:700;color:#1a1a1a'>{H(ctx.BusinessName)}</span>";

        var footer = new System.Text.StringBuilder();
        if (ctx.SupportEmail is not null)
            footer.Append($"<span>Support: <a href='mailto:{ctx.SupportEmail}' style='color:#6b7280'>{ctx.SupportEmail}</a></span> &nbsp;·&nbsp; ");
        if (ctx.WebsiteUrl is not null)
            footer.Append($"<a href='{ctx.WebsiteUrl}' style='color:#6b7280'>{H(ctx.BusinessName)}</a>");

        return $"""
            <html>
            <head><meta name="viewport" content="width=device-width,initial-scale=1"/></head>
            <body style="font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;background:#f3f4f6;margin:0;padding:0">
              <div style="max-width:580px;margin:32px auto;background:#ffffff;border-radius:10px;overflow:hidden;box-shadow:0 1px 4px rgba(0,0,0,.07)">
                <!-- Header -->
                <div style="background:#1a1a1a;padding:20px 28px">
                  {logo}
                </div>
                <!-- Body -->
                <div style="padding:28px 28px 20px">
                  {body}
                </div>
                <!-- Footer -->
                <div style="background:#f9f9f9;padding:16px 28px;border-top:1px solid #e5e7eb;font-size:12px;color:#6b7280;text-align:center">
                  {footer}
                  <p style="margin:8px 0 0">© {DateTime.UtcNow.Year} {H(ctx.BusinessName)}. All rights reserved.</p>
                </div>
              </div>
            </body></html>
            """;
    }

    private static string ViewOrderButton(OrderEmailContext ctx)
    {
        if (ctx.FrontendUrl is null) return string.Empty;
        return $"""
            <div style="margin:24px 0">
              <a href="{ctx.FrontendUrl}/orders" 
                 style="background:#1a1a1a;color:#fff;padding:12px 24px;border-radius:6px;text-decoration:none;font-weight:600;display:inline-block">
                View Order
              </a>
            </div>
            """;
    }

    // HTML-encode to prevent XSS in email bodies
    private static string H(string? s) =>
        System.Web.HttpUtility.HtmlEncode(s ?? string.Empty);

    // -----------------------------------------------------------------------
    // Transport
    // -----------------------------------------------------------------------

    private Task SendAsync(OrderEmailContext ctx, string subject, string html, CancellationToken ct)
        => SendAsync(ctx.CustomerEmail, ctx.CustomerName, subject, html, ct);

    private async Task SendAsync(
        string toEmail, string toName, string subject, string html, CancellationToken ct)
    {
        var opts = options.Value;
        try
        {
            using var client = httpClientFactory.CreateClient("Brevo");
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("api-key", opts.ApiKey);
            client.DefaultRequestHeaders.Add("accept", "application/json");

            var payload = new
            {
                sender = new { email = opts.SenderEmail, name = opts.SenderName },
                to = new[] { new { email = toEmail, name = toName } },
                subject,
                htmlContent = html
            };

            var response = await client.PostAsJsonAsync(ApiBaseUrl, payload, ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError(
                    "Brevo email send failed. StatusCode: {Code} To: {To} Subject: {Subject}",
                    (int)response.StatusCode, toEmail, subject);
            }
            else
            {
                logger.LogInformation("Email sent. To: {To} Subject: {Subject}", toEmail, subject);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error sending email to {To}", toEmail);
            // Silently swallow — Outbox handles retries, email failure must not corrupt transactions
        }
    }
}
