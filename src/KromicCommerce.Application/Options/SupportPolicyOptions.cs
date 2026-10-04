namespace KromicCommerce.Application.Options;

/// <summary>
/// Support-module policy values, bridged from the Infrastructure configuration section so
/// that Application handlers never take a dependency on Infrastructure types.
///
/// Everything here is deployment-level fact, not merchant preference. The recipient of the
/// administrative notification comes from <c>Support:AdminNotificationEmail</c> and is
/// deliberately NOT stored in the database: a storefront-facing endpoint must never be able
/// to redirect operational mail to an address of its choosing, so the address is fixed at
/// deploy time and the admin surface can only see whether it is configured.
///
/// Merchant preferences (whether to auto-generate invoices, whether to mail them, the idle
/// window) live in the SupportSettings singleton instead — see SupportSettings.
/// </summary>
public sealed class SupportPolicyOptions
{
    public const string SectionName = "Support";

    /// <summary>
    /// Address that receives "a customer needs help" notifications. Empty means the feature
    /// is not configured and notifications are skipped with a warning rather than failing
    /// the ticket creation — losing an operational email must never lose a customer ticket.
    /// </summary>
    public string AdminNotificationEmail { get; set; } = string.Empty;

    /// <summary>Display name for that recipient. Falls back to the local part of the address.</summary>
    public string AdminNotificationName { get; set; } = "Support Team";

    /// <summary>
    /// Baseline inactivity window before a resolved ticket is auto-closed. Used when the
    /// SupportSettings row has not been customised. The requirement is 72 hours.
    /// </summary>
    public int DefaultAutoCloseIdleHours { get; set; } = 72;

    /// <summary>Prefix of the human-facing ticket reference, e.g. TKT-2026-000123.</summary>
    public string TicketNumberPrefix { get; set; } = "TKT";

    /// <summary>Prefix of the human-facing invoice reference, e.g. INV-2026-000042.</summary>
    public string InvoiceNumberPrefix { get; set; } = "INV";

    /// <summary>True when an administrative recipient has been supplied.</summary>
    public bool IsAdminNotificationConfigured =>
        !string.IsNullOrWhiteSpace(AdminNotificationEmail) && AdminNotificationEmail.Contains('@');

    /// <summary>Recipient name, falling back to the address local part when no name is configured.</summary>
    public string ResolvedAdminName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(AdminNotificationName)) return AdminNotificationName.Trim();
            var local = AdminNotificationEmail.Split('@')[0];
            return string.IsNullOrWhiteSpace(local) ? "Support Team" : local;
        }
    }
}