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
    // Support desk
    // -----------------------------------------------------------------------

    public Task SendTicketAdminNotificationAsync(
        TicketAdminNotificationContext ctx, CancellationToken ct = default)
    {
        var isReopen = ctx.Kind == TicketAdminAlertKind.Reopened;

        var subject = isReopen
            ? $"[Reopened] {ctx.TicketNumber} - {ctx.Subject}"
            : $"[New] {ctx.TicketNumber} - {ctx.Subject}";

        var banner = isReopen
            ? """
              <div style="background:#fffbeb;border:1px solid #fde68a;border-radius:6px;padding:14px 16px;margin:0 0 18px">
                <p style="margin:0;font-weight:bold;color:#92400e">
                  This ticket was reopened by the customer.
                </p>
                <p style="margin:6px 0 0;color:#92400e;font-size:13px">
                  Reopen #{ctx.ReopenCount}. The thread has continued since it was last closed.
                </p>
              </div>
              """
            : string.Empty;

        var orderRow = ctx.OrderNumber is null
            ? string.Empty
            : $"""
               <tr>
                 <td style="padding:6px 0;color:#6b7280;width:130px">Order</td>
                 <td style="padding:6px 0;color:#111827;font-weight:600">{H(ctx.OrderNumber)}</td>
               </tr>
               """;

        var body = $"""
            <h1 style="color:#1a1a1a;font-size:22px;margin:0 0 4px">
              {(isReopen ? "A ticket was reopened" : "A new ticket needs a response")}
            </h1>
            <p style="color:#6b7280;font-size:13px;margin:0 0 16px">
              {ctx.OccurredAtUtc:yyyy-MM-dd HH:mm} UTC &nbsp;·&nbsp; status <strong>{H(ctx.Status)}</strong>
            </p>
            {banner}
            <div style="background:#f8f8f8;border-radius:6px;padding:16px;margin:0 0 18px">
              <tr><td style="padding:6px 0;color:#6b7280;width:130px">Ticket</td>
                  <td style="padding:6px 0;color:#111827;font-weight:700">{H(ctx.TicketNumber)}</td></tr>
              <tr><td style="padding:6px 0;color:#6b7280">Subject</td>
                  <td style="padding:6px 0;color:#111827">{H(ctx.Subject)}</td></tr>
              <tr><td style="padding:6px 0;color:#6b7280">Customer</td>
                  <td style="padding:6px 0;color:#111827">
                    {H(ctx.CustomerName)} &lt;{H(ctx.CustomerEmail)}&gt;</td></tr>
              {orderRow}
            </div>
            <p style="color:#444;margin:0 0 10px;white-space:pre-wrap">{H(ctx.Description)}</p>
            {OpenTicketButton(ctx.AdminTicketUrl, isReopen)}
            """;

        return SendAsync(ctx.AdminEmail, ctx.AdminName, subject, BuildShell(ctx, body), ct);
    }

    public Task SendTicketStatusEmailAsync(
        TicketCustomerNotificationContext ctx, CancellationToken ct = default)
    {
        var subject = ctx.Kind switch
        {
            TicketCustomerNoticeKind.Replied => $"New reply on ticket {ctx.TicketNumber}",
            TicketCustomerNoticeKind.Resolved => $"Your ticket {ctx.TicketNumber} has been resolved",
            TicketCustomerNoticeKind.Closed => $"Ticket {ctx.TicketNumber} is now closed",
            _ => $"Update on ticket {ctx.TicketNumber}"
        };

        var heading = ctx.Kind switch
        {
            TicketCustomerNoticeKind.Replied => "We have replied to your ticket",
            TicketCustomerNoticeKind.Resolved => "Your ticket has been resolved",
            TicketCustomerNoticeKind.Closed => "Your ticket is now closed",
            _ => "Your ticket has been updated"
        };

        var (tint, border, body) = ctx.Kind switch
        {
            TicketCustomerNoticeKind.Resolved => (
                "#f0fdf4", "#bbf7d0",
                """
                <p style="color:#444;margin:0 0 12px">
                  We believe the issue is now resolved. If that is not the case, simply reply to
                  this ticket and it will be reopened automatically - no need to raise a new one.
                </p>
                """),
            TicketCustomerNoticeKind.Closed => (
                "#f8f8f8", "#e5e7eb",
                """
                <p style="color:#444;margin:0 0 12px">
                  This conversation is now closed. Replying at any point in the next while will
                  reopen it with the full history intact.
                </p>
                """),
            _ => (
                "#eff6ff", "#bfdbfe",
                $"""
                 <p style="color:#444;margin:0 0 12px">
                   {H(ctx.AdminName ?? "Our team")} has replied to your ticket.
                 </p>
                 """)
        };

        var noteHtml = string.IsNullOrWhiteSpace(ctx.Note)
            ? string.Empty
            : $"""
               <div style="background:#ffffff;border-left:3px solid {border};padding:12px 14px;margin:0 0 16px">
                 <p style="margin:0;color:#444;font-size:13px;white-space:pre-wrap">{H(ctx.Note)}</p>
               </div>
               """;

        var attachmentHtml = ctx.AttachmentUrl is null
            ? string.Empty
            : $"""
               <p style="color:#6b7280;font-size:13px;margin:0 0 12px">
                 An attachment was included with the reply:
                 <a href="{H(ctx.AttachmentUrl)}" style="color:#2563eb">view it</a>
               </p>
               """;

        var content = $"""
            <h1 style="color:#1a1a1a;font-size:22px;margin:0 0 16px">{heading}</h1>
            <div style="background:{tint};border:1px solid {border};border-radius:6px;padding:16px;margin:0 0 18px">
              <p style="margin:0 0 4px">
                <strong style="color:#111827">{H(ctx.TicketNumber)}</strong>
                &nbsp;&middot;&nbsp;
                <span style="color:#6b7280;font-size:13px">{H(ctx.Subject)}</span>
              </p>
              <p style="margin:6px 0 0;color:#6b7280;font-size:13px">{ctx.OccurredAtUtc:yyyy-MM-dd HH:mm} UTC</p>
            </div>
            {body}
            {noteHtml}
            {attachmentHtml}
            {ViewTicketButton(ctx.FrontendTicketUrl)}
            """;

        return SendAsync(ctx.CustomerEmail, ctx.CustomerName, subject, BuildShell(ctx, content), ct);
    }

    public Task SendInvoiceEmailAsync(InvoiceMailContext ctx, CancellationToken ct = default)
    {
        return SendWithAttachmentAsync(
            ctx.CustomerEmail,
            ctx.CustomerName,
            ctx.Subject,
            ctx.HtmlBody,
            ctx.PdfBytes,
            ctx.FileName,
            ct);
    }

    private static string OpenTicketButton(string? url, bool isReopen)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;

        var label = isReopen ? "Open Ticket" : "View Ticket";
        return $"""
            <div style="margin:24px 0">
              <a href="{H(url)}"
                 style="background:#1a1a1a;color:#fff;padding:12px 24px;border-radius:6px;text-decoration:none;font-weight:600;display:inline-block">
                {label}
              </a>
            </div>
            """;
    }

    private static string ViewTicketButton(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;
        return $"""
            <div style="margin:24px 0">
              <a href="{H(url)}"
                 style="background:#1a1a1a;color:#fff;padding:12px 24px;border-radius:6px;text-decoration:none;font-weight:600;display:inline-block">
                View Ticket
              </a>
            </div>
            """;
    }

    /// <summary>Wraps support-desk bodies in the same shell as transactional order email.</summary>
    private string BuildShell(TicketAdminNotificationContext ctx, string body) => BuildEmail(
        new OrderEmailContext(
            ctx.AdminEmail, ctx.AdminName, ctx.TicketNumber, 0m, string.Empty,
            ctx.BusinessName, ctx.LogoUrl, null, null, null), body);

    private string BuildShell(TicketCustomerNotificationContext ctx, string body) => BuildEmail(
        new OrderEmailContext(
            ctx.CustomerEmail, ctx.CustomerName, ctx.TicketNumber, 0m, string.Empty,
            ctx.BusinessName, ctx.LogoUrl, ctx.SupportEmail, null, ctx.FrontendTicketUrl), body);

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
        var payload = new
        {
            sender = new { email = options.Value.SenderEmail, name = options.Value.SenderName },
            to = new[] { new { email = toEmail, name = toName } },
            subject,
            htmlContent = html
        };

        await PostAsync(payload, toEmail, subject, ct);
    }

    /// <summary>
    /// Sends a message with one binary attachment.
    ///
    /// Brevo accepts attachments inline as base64 rather than by URL. That is what makes the
    /// invoice pipeline self-contained: the PDF lives in our database, so there is no public URL
    /// to hand the provider, and no window in which a document could be fetched by anyone who
    /// guessed the link. The cost is roughly a third again on the request body, which for a
    /// few-kilobyte invoice is irrelevant.
    /// </summary>
    private async Task SendWithAttachmentAsync(
        string toEmail,
        string toName,
        string subject,
        string html,
        byte[] attachment,
        string fileName,
        CancellationToken ct)
    {
        var payload = new
        {
            sender = new { email = options.Value.SenderEmail, name = options.Value.SenderName },
            to = new[] { new { email = toEmail, name = toName } },
            subject,
            htmlContent = html,
            attachment = new[]
            {
                new
                {
                    name = string.IsNullOrWhiteSpace(fileName) ? "invoice.pdf" : fileName,
                    content = Convert.ToBase64String(attachment)
                }
            }
        };

        await PostAsync(payload, toEmail, subject, ct);
    }

    private async Task PostAsync<TPayload>(
        TPayload payload, string toEmail, string subject, CancellationToken ct)
    {
        var opts = options.Value;
        try
        {
            using var client = httpClientFactory.CreateClient("Brevo");
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("api-key", opts.ApiKey);
            client.DefaultRequestHeaders.Add("accept", "application/json");

            var response = await client.PostAsJsonAsync(ApiBaseUrl, payload, ct);

            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("Email sent. To: {To} Subject: {Subject}", toEmail, subject);
            }
            else
            {
                // Status only. The response body can echo the payload, which for an invoice
                // includes the customer's address and the document itself.
                logger.LogError(
                    "Brevo email send failed. StatusCode: {Code} To: {To} Subject: {Subject}",
                    (int)response.StatusCode, toEmail, subject);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error sending email to {To}", toEmail);
            // Silently swallow — Outbox handles retries, email failure must not corrupt transactions
        }
    }
}
