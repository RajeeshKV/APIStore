using System.Text.Json;
using KromicCommerce.Application.Abstractions.Email;
using KromicCommerce.Application.Features.Leads;
using KromicCommerce.Application.Options;
using KromicCommerce.Infrastructure.Configuration;
using KromicCommerce.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KromicCommerce.Infrastructure.BackgroundServices;

/// <summary>
/// Dispatch for lead-capture notifications.
/// </summary>
internal sealed partial class OutboxProcessor
{
    private async Task DispatchLeadAsync(
        OutboxEvent evt,
        AppDbContext db,
        IEmailService emailSvc,
        CancellationToken ct)
    {
        var payload = Read<LeadSubmittedPayload>(evt);
        if (payload is null) return;

        // A lead is a durable record, so this event is sent at most once. The outbox can retry a
        // send whose acknowledgement was lost; without this guard a transient provider timeout
        // puts a duplicate "new enquiry" in a real inbox, and a sales team that cannot tell which
        // message is current will follow up twice.
        var lead = await db.Leads.AsNoTracking().FirstOrDefaultAsync(l => l.Id == payload.LeadId, ct);
        if (lead is null) return;

        if (lead.NotifiedAt)
        {
            logger.LogDebug("Lead {LeadId} was already notified. Skipping.", payload.LeadId);
            return;
        }

        var recipient = LeadOptions.NotificationEmail;

        if (string.IsNullOrWhiteSpace(recipient))
        {
            // The lead row is still there, so this is recoverable: fix the configuration and the
            // next submission notifies normally. Retrying an event with no destination would
            // just burn every retry and end as permanently failed, hiding a config mistake
            // behind what looks like a provider outage.
            logger.LogWarning(
                "Lead {LeadId} has no notification recipient configured " +
                "(Leads__NotificationEmail). The lead is stored but no email was sent.",
                payload.LeadId);
            return;
        }

        await emailSvc.SendLeadNotificationAsync(
            new LeadNotificationContext(
                recipient,
                string.IsNullOrWhiteSpace(LeadOptions.NotificationName)
                    ? "Website Enquiries"
                    : LeadOptions.NotificationName,
                payload.Name,
                payload.Phone,
                payload.PhoneRaw,
                payload.Email,
                payload.Business,
                payload.Source,
                payload.SubmittedAtUtc),
            ct);

        // Marked on the lead, not only by the caller marking the outbox row processed. If that
        // update is lost the event is retried, and this flag is what stops a second email —
        // provided this statement commits first.
        lead.MarkNotified();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Lead-capture recipient and naming, supplied by Leads configuration.
    /// </summary>
    private LeadPolicyOptions LeadOptions => leadOptions.Value;
}