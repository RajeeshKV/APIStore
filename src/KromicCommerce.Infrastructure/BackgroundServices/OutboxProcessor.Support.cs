using System.Text.Json;
using KromicCommerce.Application.Abstractions.Email;
using KromicCommerce.Application.Abstractions.Store;
using KromicCommerce.Application.Features.Support;
using KromicCommerce.Application.Options;
using KromicCommerce.Domain.Store;
using KromicCommerce.Infrastructure.Configuration;
using KromicCommerce.Infrastructure.Persistence;

namespace KromicCommerce.Infrastructure.BackgroundServices;

/// <summary>
/// Support-desk half of <see cref="OutboxProcessor"/>.
///
/// Split into its own file because the support desk is a self-contained concern with its own
/// rules — a configured-at-deploy-time recipient, a merchant-controlled mailing toggle, and a
/// database-backed attachment — and folding that into the order switch would bury the order
/// cases under unrelated branching.
///
/// TWO INDEPENDENT GATES GUARD EVERY SEND:
///   - The administrative recipient comes from <c>Support:AdminNotificationEmail</c> only.
///   - Whether an invoice is mailed at all comes from SupportSettings at dispatch time.
/// Both are read here, at the moment of sending, rather than at the moment of queueing, so a
/// setting changed in the last few seconds takes effect.
/// </summary>
internal sealed partial class OutboxProcessor
{
    /// <summary>Administrative notification recipient and naming. Supplied by SupportOptions.</summary>
    private SupportPolicyOptions SupportNotifierOptions => supportOptions.Value;

    /// <summary>
    /// Storefront base URL, used to build the deep links that let a reader jump straight into the
    /// conversation instead of searching for a reference number.
    /// </summary>
    private string SupportFrontendUrl => deployOptions.Value.FrontendUrl ?? string.Empty;

    private async Task DispatchSupportAsync(
        OutboxEvent evt,
        AppDbContext db,
        IEmailService emailSvc,
        BusinessSettings? settings,
        AppPublicOptions appOptions,
        CancellationToken ct)
    {
        var support = SupportNotifierOptions;

        switch (evt.EventType)
        {
            case TicketOutbox.Created:
            {
                var payload = Read<TicketOpenedPayload>(evt);
                if (payload is null) return;

                if (!AdminNotificationsAvailable(support, payload.TicketNumber)) return;

                await emailSvc.SendTicketAdminNotificationAsync(
                    new TicketAdminNotificationContext(
                        AdminEmail: support.AdminNotificationEmail.Trim(),
                        AdminName: support.ResolvedAdminName,
                        Kind: TicketAdminAlertKind.Created,
                        TicketId: payload.TicketId.ToString(),
                        TicketNumber: payload.TicketNumber,
                        Subject: payload.Subject,
                        CustomerName: payload.CustomerName,
                        CustomerEmail: payload.CustomerEmail,
                        Description: payload.Description,
                        Status: nameof(TicketStatus.Open),
                        ReopenCount: 0,
                        OrderNumber: payload.OrderNumber,
                        OccurredAtUtc: payload.OccurredAtUtc,
                        AdminTicketUrl: AdminTicketUrl(payload.TicketId),
                        BusinessName: settings?.BusinessName ?? "Store",
                        LogoUrl: settings?.LogoUrl),
                    ct);

                break;
            }

            case TicketOutbox.Reopened:
            {
                var payload = Read<TicketReopenedPayload>(evt);
                if (payload is null) return;

                if (!AdminNotificationsAvailable(support, payload.TicketNumber)) return;

                await emailSvc.SendTicketAdminNotificationAsync(
                    new TicketAdminNotificationContext(
                        AdminEmail: support.AdminNotificationEmail.Trim(),
                        AdminName: support.ResolvedAdminName,
                        Kind: TicketAdminAlertKind.Reopened,
                        TicketId: payload.TicketId.ToString(),
                        TicketNumber: payload.TicketNumber,
                        Subject: payload.Subject,
                        CustomerName: payload.CustomerName,
                        CustomerEmail: payload.CustomerEmail,

                        // The opening description is not carried on this payload. Repeating a
                        // stale first message would misrepresent whatever the customer is
                        // actually asking about now, so the alert links to the thread instead.
                        Description: "The customer reopened this conversation. Open the ticket to read the full thread.",
                        Status: nameof(TicketStatus.Open),
                        ReopenCount: payload.ReopenCount,
                        OrderNumber: payload.OrderNumber,
                        OccurredAtUtc: payload.OccurredAtUtc,
                        AdminTicketUrl: AdminTicketUrl(payload.TicketId),
                        BusinessName: settings?.BusinessName ?? "Store",
                        LogoUrl: settings?.LogoUrl),
                    ct);

                break;
            }

            case TicketOutbox.Resolved:
            {
                var payload = Read<TicketResolvedPayload>(evt);
                if (payload is null) return;

                await emailSvc.SendTicketStatusEmailAsync(
                    CustomerNotice(
                        payload.CustomerEmail,
                        payload.CustomerName,
                        TicketCustomerNoticeKind.Resolved,
                        payload.TicketId,
                        payload.TicketNumber,
                        payload.Subject,
                        nameof(TicketStatus.Resolved),
                        adminName: payload.AdminName,
                        note: payload.ResolutionNote,
                        occurredAtUtc: payload.OccurredAtUtc,
                        settings: settings),
                    ct);

                break;
            }

            case TicketOutbox.Closed:
            {
                var payload = Read<TicketClosedPayload>(evt);
                if (payload is null) return;

                await emailSvc.SendTicketStatusEmailAsync(
                    CustomerNotice(
                        payload.CustomerEmail,
                        payload.CustomerName,
                        TicketCustomerNoticeKind.Closed,
                        payload.TicketId,
                        payload.TicketNumber,
                        payload.Subject,
                        nameof(TicketStatus.Closed),
                        adminName: null,
                        note: payload.Note,
                        occurredAtUtc: payload.OccurredAtUtc,
                        settings: settings),
                    ct);

                break;
            }

            case TicketOutbox.CommentPosted:
            {
                var payload = Read<TicketCommentPayload>(evt);
                if (payload is null) return;

                // Internal notes never produce this event, so a payload always represents a
                // public message. The recipient is whoever did not write it.
                if (payload.IsAdminAuthor)
                {
                    await emailSvc.SendTicketStatusEmailAsync(
                        CustomerNotice(
                            payload.CustomerEmail,
                            payload.CustomerName,
                            TicketCustomerNoticeKind.Replied,
                            payload.TicketId,
                            payload.TicketNumber,
                            payload.Subject,
                            nameof(TicketStatus.Open),
                            adminName: payload.AdminName,
                            note: payload.Body,
                            attachmentUrl: payload.AttachmentUrl,
                            occurredAtUtc: payload.OccurredAtUtc,
                            settings: settings),
                        ct);
                }
                else
                {
                    if (!AdminNotificationsAvailable(support, payload.TicketNumber)) return;

                    await emailSvc.SendTicketAdminNotificationAsync(
                        new TicketAdminNotificationContext(
                            AdminEmail: support.AdminNotificationEmail.Trim(),
                            AdminName: support.ResolvedAdminName,
                            Kind: TicketAdminAlertKind.CustomerReplied,
                            TicketId: payload.TicketId.ToString(),
                            TicketNumber: payload.TicketNumber,
                            Subject: payload.Subject,
                            CustomerName: payload.CustomerName,
                            CustomerEmail: payload.CustomerEmail,
                            Description: payload.Body,
                            Status: nameof(TicketStatus.Open),
                            ReopenCount: 0,
                            OrderNumber: null,
                            OccurredAtUtc: payload.OccurredAtUtc,
                            AdminTicketUrl: AdminTicketUrl(payload.TicketId),
                            BusinessName: settings?.BusinessName ?? "Store",
                            LogoUrl: settings?.LogoUrl),
                        ct);
                }

                break;
            }

            case TicketOutbox.InvoiceGenerated:
            {
                await SendInvoiceAsync(evt, db, emailSvc, settings, ct);
                break;
            }
        }
    }

    // -----------------------------------------------------------------------
    // Invoice delivery
    // -----------------------------------------------------------------------

    /// <summary>
    /// Mails a rendered invoice, unless the merchant has turned automated mailing off.
    ///
    /// Generation and delivery are deliberately separate switches: "stop emailing my customers"
    /// and "stop producing my invoices" are different requests, and a merchant who only wants
    /// the first must still be able to download the document. So the toggle suppresses this
    /// method and nothing else.
    /// </summary>
    private async Task SendInvoiceAsync(
        OutboxEvent evt,
        AppDbContext db,
        IEmailService emailSvc,
        BusinessSettings? settings,
        CancellationToken ct)
    {
        var payload = Read<TicketInvoiceGeneratedPayload>(evt);
        if (payload is null) return;

        // The outbox can retry a send whose acknowledgement was lost. Without this guard a
        // transient provider timeout would produce a second invoice email, which to a customer
        // reads as a billing system that cannot count.
        if (payload.EmailedAlready) return;

        var supportSettings = await db.SupportSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == SupportSettings.SingletonId, ct);

        if (supportSettings is null || !supportSettings.AutomatedInvoiceMailingEnabled)
        {
            logger.LogInformation(
                "Automated invoice mailing is disabled. Invoice {InvoiceNumber} was generated and remains " +
                "downloadable, but was not emailed.",
                payload.InvoiceNumber);
            return;
        }

        var invoice = await db.TicketInvoices
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == payload.InvoiceId, ct);

        if (invoice is null || invoice.PdfContent is not { Length: > 0 })
        {
            logger.LogWarning(
                "Invoice {InvoiceNumber} has no rendered document; nothing to mail.", payload.InvoiceNumber);
            return;
        }

        var subject = string.IsNullOrWhiteSpace(supportSettings.InvoiceMailSubjectOverride)
            ? $"Invoice {payload.InvoiceNumber} - ticket {payload.TicketNumber}"
            : supportSettings.InvoiceMailSubjectOverride;

        await emailSvc.SendInvoiceEmailAsync(
            new InvoiceMailContext(
                CustomerEmail: payload.CustomerEmail,
                CustomerName: string.IsNullOrWhiteSpace(payload.CustomerName) ? "Customer" : payload.CustomerName,
                InvoiceNumber: payload.InvoiceNumber,
                TicketNumber: payload.TicketNumber,
                OrderNumber: payload.OrderNumber,
                CurrencyCode: payload.CurrencyCode,
                GrandTotal: payload.GrandTotal,
                Subject: subject,
                HtmlBody: InvoiceEmailHtml(payload, invoice.FileName, settings),
                PdfBytes: invoice.PdfContent,
                FileName: invoice.FileName,
                BusinessName: settings?.BusinessName ?? "Store",
                LogoUrl: settings?.LogoUrl,
                SupportEmail: settings?.SupportEmail,
                WebsiteUrl: settings?.WebsiteUrl),
            ct);

        // Persisted in its own statement so the send result survives independently of the
        // outbox row being marked processed. If this update is lost the event is retried, and
        // the EmailedToCustomer check at the top of the next attempt stops a second email —
        // provided the update landed. Losing that race sends a duplicate; losing the update
        // itself would, so it is written before the event is marked processed by the caller.
        await db.TicketInvoices
            .Where(i => i.Id == payload.InvoiceId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(i => i.EmailedToCustomer, true)
                    .SetProperty(i => i.EmailedAtUtc, DateTime.UtcNow),
                ct);
    }

    private string InvoiceEmailHtml(
        TicketInvoiceGeneratedPayload payload, string fileName, BusinessSettings? settings)
    {
        var business = settings?.BusinessName ?? "Store";
        var supportEmail = settings?.SupportEmail;
        var ticketLink = FrontendTicketUrl(payload.TicketId);

        var orderRow = string.IsNullOrWhiteSpace(payload.OrderNumber)
            ? string.Empty
            : $"<p style='margin:0 0 6px'><strong>Order:</strong> {H(payload.OrderNumber)}</p>";

        var link = string.IsNullOrWhiteSpace(ticketLink)
            ? string.Empty
            : $"""
               <div style="margin:24px 0">
                 <a href="{H(ticketLink)}"
                    style="background:#1a1a1a;color:#fff;padding:12px 24px;border-radius:6px;text-decoration:none;font-weight:600;display:inline-block">
                   View Ticket
                 </a>
               </div>
               """;

        var contact = string.IsNullOrWhiteSpace(supportEmail)
            ? string.Empty
            : $"<span>Support: <a href='mailto:{H(supportEmail)}' style='color:#6b7280'>{H(supportEmail)}</a></span> &nbsp;·&nbsp; ";

        return $"""
            <html>
            <head><meta name="viewport" content="width=device-width,initial-scale=1"/></head>
            <body style="font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;background:#f3f4f6;margin:0;padding:0">
              <div style="max-width:580px;margin:32px auto;background:#ffffff;border-radius:10px;overflow:hidden;box-shadow:0 1px 4px rgba(0,0,0,.07)">
                <div style="background:#1a1a1a;padding:20px 28px">
                  <span style="font-size:18px;font-weight:700;color:#fff">{H(business)}</span>
                </div>
                <div style="padding:28px 28px 20px">
                  <h1 style="color:#1a1a1a;font-size:22px;margin:0 0 16px">Your invoice is attached</h1>
                  <p style="color:#444;margin:0 0 12px">
                    Hi {H(payload.CustomerName)}, the invoice for ticket
                    <strong>{H(payload.TicketNumber)}</strong> is attached to this email.
                  </p>
                  <div style="background:#f8f8f8;border-radius:6px;padding:16px;margin:20px 0">
                    <p style="margin:0 0 6px"><strong>Invoice number:</strong> {H(payload.InvoiceNumber)}</p>
                    {orderRow}
                    <p style="margin:0"><strong>Amount due:</strong> {payload.GrandTotal:F2} {H(payload.CurrencyCode)}</p>
                  </div>
                  <p style="color:#444;margin:0 0 8px">
                    The document is attached as <strong>{H(fileName)}</strong>. Please keep it for your records.
                  </p>
                  {link}
                </div>
                <div style="background:#f9f9f9;padding:16px 28px;border-top:1px solid #e5e7eb;font-size:12px;color:#6b7280;text-align:center">
                  {contact}
                  <p style="margin:8px 0 0">© {DateTime.UtcNow.Year} {H(business)}. All rights reserved.</p>
                </div>
              </div>
            </body></html>
            """;
    }

    // -----------------------------------------------------------------------
    // Shared helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Reads and deserialises an outbox payload, logging rather than throwing on malformed JSON.
    ///
    /// A payload that cannot be read is a bug that no retry can fix, so throwing would burn the
    /// retry budget and delay every event behind it in the batch for no benefit.
    /// </summary>
    private T? Read<T>(OutboxEvent evt)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(evt.Payload);
        }
        catch (JsonException ex)
        {
            logger.LogError(
                ex, "Outbox payload for {Type} could not be deserialized; dropping event {Id}.",
                evt.EventType, evt.Id);
            return default;
        }
    }

    /// <summary>
    /// Whether an administrative notification can be sent at all.
    ///
    /// A missing recipient is a deployment misconfiguration, not a transient fault, so the event
    /// is dropped after one loud log rather than retried. Retrying would only stall the queue
    /// behind it for a condition that will not change on its own.
    /// </summary>
    private bool AdminNotificationsAvailable(SupportPolicyOptions support, string ticketNumber)
    {
        if (support.IsAdminNotificationConfigured) return true;

        logger.LogError(
            "Support:AdminNotificationEmail is not configured. Notification for ticket {TicketNumber} dropped. " +
            "Set the variable and restart to receive support alerts.",
            ticketNumber);

        return false;
    }

    private TicketCustomerNotificationContext CustomerNotice(
        string email,
        string name,
        TicketCustomerNoticeKind kind,
        Guid ticketId,
        string ticketNumber,
        string subject,
        string status,
        string? adminName,
        string? note,
        DateTime occurredAtUtc,
        BusinessSettings? settings,
        string? attachmentUrl = null)
        => new(
            CustomerEmail: email,
            CustomerName: name,
            Kind: kind,
            TicketId: ticketId.ToString(),
            TicketNumber: ticketNumber,
            Subject: subject,
            Status: status,
            AdminName: adminName,
            Note: note,
            AttachmentUrl: attachmentUrl,
            OccurredAtUtc: occurredAtUtc,
            FrontendTicketUrl: FrontendTicketUrl(ticketId),
            BusinessName: settings?.BusinessName ?? "Store",
            LogoUrl: settings?.LogoUrl,
            SupportEmail: settings?.SupportEmail);

    /// <summary>Deep link into the storefront ticket thread.</summary>
    private string? FrontendTicketUrl(Guid ticketId) =>
        string.IsNullOrWhiteSpace(SupportFrontendUrl)
            ? null
            : $"{SupportFrontendUrl.TrimEnd('/')}/support/{ticketId}";

    /// <summary>Deep link into the administrative ticket queue.</summary>
    private string? AdminTicketUrl(Guid ticketId) =>
        string.IsNullOrWhiteSpace(SupportFrontendUrl)
            ? null
            : $"{SupportFrontendUrl.TrimEnd('/')}/admin/support/{ticketId}";

    private static string H(string? s) => System.Web.HttpUtility.HtmlEncode(s ?? string.Empty);
}