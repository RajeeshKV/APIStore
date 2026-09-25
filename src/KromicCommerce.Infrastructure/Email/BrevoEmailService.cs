using System.Net.Http.Json;
using KromicCommerce.Application.Abstractions.Email;
using KromicCommerce.Infrastructure.Configuration;

namespace KromicCommerce.Infrastructure.Email;

/// <summary>
/// Brevo (formerly Sendinblue) email implementation of IEmailService.
/// Supports two modes from BusinessSettings.Email.Mode:
///   KromicManaged — uses Kromic's shared Brevo account/API key (from BrevoOptions).
///   CustomerBrevo — uses the customer's own Brevo API key (also from BrevoOptions,
///                    but the key was configured by the store admin).
/// Email templates are code-managed — no database HTML editor in Phase 5.
/// API key is never logged. Only send result status is logged.
/// </summary>
internal sealed class BrevoEmailService(
    IOptions<BrevoOptions> options,
    IHttpClientFactory httpClientFactory,
    ILogger<BrevoEmailService> logger) : IEmailService
{
    private const string ApiBaseUrl = "https://api.brevo.com/v3/smtp/email";

    public async Task SendOrderConfirmationAsync(
        OrderEmailContext ctx, CancellationToken cancellationToken = default)
    {
        var subject = $"Order Confirmed — {ctx.OrderNumber}";
        var html = BuildOrderEmail(ctx,
            $"<h2>Thank you for your order!</h2>" +
            $"<p>Your order <strong>{ctx.OrderNumber}</strong> has been placed successfully.</p>" +
            $"<p>Total: <strong>{ctx.GrandTotal:F2} {ctx.Currency}</strong></p>");

        await SendAsync(ctx.CustomerEmail, ctx.CustomerName, subject, html, cancellationToken);
    }

    public async Task SendPaymentConfirmationAsync(
        OrderEmailContext ctx, CancellationToken cancellationToken = default)
    {
        var subject = $"Payment Received — {ctx.OrderNumber}";
        var html = BuildOrderEmail(ctx,
            $"<h2>Payment Confirmed</h2>" +
            $"<p>We have received your payment of <strong>{ctx.GrandTotal:F2} {ctx.Currency}</strong>" +
            $" for order <strong>{ctx.OrderNumber}</strong>.</p>");

        await SendAsync(ctx.CustomerEmail, ctx.CustomerName, subject, html, cancellationToken);
    }

    public async Task SendPaymentFailedAsync(
        OrderEmailContext ctx, string? reason, CancellationToken cancellationToken = default)
    {
        var subject = $"Payment Failed — {ctx.OrderNumber}";
        var html = BuildOrderEmail(ctx,
            $"<h2>Payment Unsuccessful</h2>" +
            $"<p>Your payment for order <strong>{ctx.OrderNumber}</strong> could not be processed.</p>" +
            (reason is not null ? $"<p>Reason: {System.Web.HttpUtility.HtmlEncode(reason)}</p>" : ""));

        await SendAsync(ctx.CustomerEmail, ctx.CustomerName, subject, html, cancellationToken);
    }

    public async Task SendOrderShippedAsync(
        OrderEmailContext ctx, string? trackingNumber, string? provider, CancellationToken cancellationToken = default)
    {
        var subject = $"Your Order Has Shipped — {ctx.OrderNumber}";
        var trackingInfo = trackingNumber is not null
            ? $"<p>Tracking: <strong>{trackingNumber}</strong>" +
              (provider is not null ? $" via {provider}" : "") + "</p>"
            : "";
        var html = BuildOrderEmail(ctx,
            $"<h2>Order Shipped!</h2>" +
            $"<p>Your order <strong>{ctx.OrderNumber}</strong> is on its way.</p>" +
            trackingInfo);

        await SendAsync(ctx.CustomerEmail, ctx.CustomerName, subject, html, cancellationToken);
    }

    public async Task SendOrderCancelledAsync(
        OrderEmailContext ctx, string? reason,
        CancellationToken cancellationToken = default)
    {
        var subject = $"Your Order Has Been Cancelled — {ctx.OrderNumber}";
        var reasonHtml = reason is not null
            ? $"<p>Reason: {System.Web.HttpUtility.HtmlEncode(reason)}</p>"
            : "";
        var html = BuildOrderEmail(ctx,
            $"<h2>Order Cancelled</h2>" +
            $"<p>Your order <strong>{ctx.OrderNumber}</strong> has been cancelled.</p>" +
            reasonHtml +
            $"<p>If you have any questions, please contact us.</p>");

        await SendAsync(ctx.CustomerEmail, ctx.CustomerName, subject, html, cancellationToken);
    }

    public async Task SendPasswordResetAsync(
        string toEmail, string recipientName, string resetToken,
        CancellationToken cancellationToken = default)
    {
        // resetToken is NEVER logged — only used in the email body
        var subject = "Password Reset Request";
        var html = $"""
            <html><body style='font-family:sans-serif;max-width:600px;margin:auto'>
              <div style='padding:20px'>
                <h2>Password Reset</h2>
                <p>Hi {System.Web.HttpUtility.HtmlEncode(recipientName)},</p>
                <p>We received a request to reset your password.
                   Use the token below in the password reset form. It expires in 15 minutes.</p>
                <p style='font-size:1.4em;letter-spacing:2px;font-weight:bold'>
                  {System.Web.HttpUtility.HtmlEncode(resetToken)}
                </p>
                <p>If you did not request this, you can safely ignore this email.</p>
              </div>
            </body></html>
            """;

        await SendAsync(toEmail, recipientName, subject, html, cancellationToken);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private string BuildOrderEmail(OrderEmailContext ctx, string body)
    {
        var logo = ctx.LogoUrl is not null
            ? $"<img src='{ctx.LogoUrl}' alt='{ctx.BusinessName}' style='max-height:50px;'/>"
            : $"<strong>{ctx.BusinessName}</strong>";
        return $"""
            <html><body style='font-family:sans-serif;max-width:600px;margin:auto'>
              <div style='padding:20px;border-bottom:1px solid #eee'>{logo}</div>
              <div style='padding:20px'>{body}</div>
              {(ctx.SupportEmail is not null ? $"<p style='color:#888'>Support: {ctx.SupportEmail}</p>" : "")}
              {(ctx.WebsiteUrl is not null ? $"<p><a href='{ctx.WebsiteUrl}'>{ctx.WebsiteUrl}</a></p>" : "")}
            </body></html>
            """;
    }

    private async Task SendAsync(
        string toEmail, string toName, string subject, string html,
        CancellationToken cancellationToken)
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

            var response = await client.PostAsJsonAsync(ApiBaseUrl, payload, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogError(
                    "Brevo email send failed. StatusCode: {Code} To: {To}",
                    response.StatusCode, toEmail);
                // Do not throw — email failure must not corrupt the core order transaction
            }
            else
            {
                logger.LogInformation("Email sent via Brevo. To: {To} Subject: {Subject}", toEmail, subject);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error sending email via Brevo to {To}", toEmail);
            // Silently swallow — caller should use Outbox for reliable delivery
        }
    }
}
