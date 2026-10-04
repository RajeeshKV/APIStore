using System.ComponentModel.DataAnnotations;

namespace KromicCommerce.Infrastructure.Configuration;

/// <summary>
/// Deployment-level support desk configuration, supplied entirely from environment variables.
///
/// <para>
/// The administrative notification recipient is deliberately fixed here rather than stored in the
/// database. A storefront-facing endpoint that could rewrite the destination of operational mail
/// would be an open relay for anything the admin UI can post — and unlike merchant preferences,
/// there is no legitimate reason for a store owner to change it from inside the application.
/// The admin surface reports only whether an address is configured, and masks it.
/// </para>
///
/// <para>
/// Deliberately NOT validated at startup, unlike JWT and the database. A missing address must
/// not stop a deployment from booting: the support desk still opens tickets, still threads
/// replies and still invoices. What degrades is the administrative notification, and that is
/// logged loudly on every ticket so the gap is impossible to miss.
/// </para>
/// </summary>
public sealed class SupportOptions
{
    public const string SectionName = "Support";

    /// <summary>
    /// Address that receives new-ticket and reopened-ticket notifications.
    /// Set as <c>Support__AdminNotificationEmail</c>.
    /// </summary>
    [EmailAddress]
    public string AdminNotificationEmail { get; set; } = string.Empty;

    /// <summary>Display name for that recipient. Falls back to the address local part.</summary>
    public string AdminNotificationName { get; set; } = "Support Team";

    /// <summary>
    /// Baseline inactivity window in hours before the worker closes a resolved ticket.
    /// The requirement is 72. A merchant can change it at runtime from SupportSettings; this
    /// is the value a fresh deployment starts with.
    /// </summary>
    [Range(1, 720)]
    public int DefaultAutoCloseIdleHours { get; set; } = 72;

    /// <summary>Prefix of the quoted ticket reference. Produces e.g. TKT-2026-000123.</summary>
    public string TicketNumberPrefix { get; set; } = "TKT";

    /// <summary>Prefix of the quoted invoice reference. Produces e.g. INV-2026-000042.</summary>
    public string InvoiceNumberPrefix { get; set; } = "INV";
}