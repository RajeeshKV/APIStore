namespace KromicCommerce.Domain.Support;

/// <summary>
/// Single-row, admin-editable configuration for the support module.
///
/// Split from <see cref="KromicCommerce.Domain.Store.BusinessSettings"/> on purpose: business
/// settings describe how the store trades, while these values change how support automation
/// behaves and are reviewed on a different cadence. Keeping them apart means a merchant
/// updating their logo cannot accidentally reset the invoice mailing toggle.
///
/// Environment variables remain the source of truth for deployment-level facts that the
/// merchant must not change at runtime — above all the administrator notification address
/// and the mail/SMTP credentials. What lives here is the policy a store owner legitimately
/// wants to flip from the admin UI.
///
/// Like <see cref="KromicCommerce.Domain.Store.BusinessSettings"/>, this is a fixed-id
/// singleton so that read paths never need a "find the current row" query and cannot race
/// each other into creating two.
/// </summary>
public sealed class SupportSettings : AuditableEntity
{
    /// <summary>Singleton row id — identical across every deployment.</summary>
    public static readonly Guid SingletonId = new("00000000-0000-0000-0000-0000000000F1");

    /// <summary>Inactivity window before the worker closes a resolved ticket, in hours.</summary>
    public const int MinAutoCloseIdleHours = 1;

    /// <summary>Upper bound on the auto-close window. Beyond a month a "resolved" ticket is not really resolved.</summary>
    public const int MaxAutoCloseIdleHours = 24 * 30;

    private SupportSettings() { } // EF constructor

    public static SupportSettings CreateDefault(int autoCloseIdleHours = TicketStatusHistory.DefaultAutoCloseIdleHours)
        => new()
        {
            Id = SingletonId,
            AutoCloseIdleHours = ClampIdleHours(autoCloseIdleHours),

            // Enabled by default because the invoice pipeline is a headline feature; a merchant
            // who does not want it switches it off, rather than discovering it was silently on.
            AutomatedInvoiceMailingEnabled = true,
            InvoiceMailSubjectOverride = null,
            AutoGenerateInvoiceOnResolve = true,

            NotifyAdminOnTicketCreated = true,
            NotifyAdminOnTicketReopened = true,
            NotifyCustomerOnTicketResolved = true,

            MaxAttachmentsPerComment = TicketComment.MaxAttachments
        };

    // -----------------------------------------------------------------------
    // Automated closure
    // -----------------------------------------------------------------------

    /// <summary>
    /// Hours of customer silence after resolution before the worker closes the ticket.
    /// Defaults to 72. Read at resolve time and stamped onto the ticket so a later settings
    /// change cannot retroactively move a deadline that is already running.
    /// </summary>
    public int AutoCloseIdleHours { get; private set; } = TicketStatusHistory.DefaultAutoCloseIdleHours;

    // -----------------------------------------------------------------------
    // Automated invoicing
    // -----------------------------------------------------------------------

    /// <summary>
    /// Global kill switch for mailing a generated invoice to the customer. Disabling it stops
    /// the email only — generation still runs, so the document is always available for manual
    /// download. This is the "toggleable configuration setting" the admin surface exposes.
    /// </summary>
    public bool AutomatedInvoiceMailingEnabled { get; private set; } = true;

    /// <summary>Optional replacement subject line for the invoice email. Null uses the default.</summary>
    public string? InvoiceMailSubjectOverride { get; private set; }

    /// <summary>
    /// Whether resolving a ticket queues an invoice. Independent of the mailing toggle: a
    /// merchant may want invoices produced but delivered by hand.
    /// </summary>
    public bool AutoGenerateInvoiceOnResolve { get; private set; } = true;

    // -----------------------------------------------------------------------
    // Notification policy
    // The recipient address itself is NOT here — it comes from Support:AdminNotificationEmail
    // in configuration, because a storefront-facing endpoint must never be able to redirect
    // operational mail to an address of its choosing.
    // -----------------------------------------------------------------------

    public bool NotifyAdminOnTicketCreated { get; private set; } = true;
    public bool NotifyAdminOnTicketReopened { get; private set; } = true;
    public bool NotifyCustomerOnTicketResolved { get; private set; } = true;

    // -----------------------------------------------------------------------
    // Media
    // -----------------------------------------------------------------------

    /// <summary>Per-comment media ceiling. Clamped to the domain ceiling of 6.</summary>
    public int MaxAttachmentsPerComment { get; private set; } = TicketComment.MaxAttachments;

    // -----------------------------------------------------------------------
    // Behaviour
    // -----------------------------------------------------------------------

    public void UpdateAutomation(
        bool? autoGenerateInvoiceOnResolve = null,
        bool? automatedInvoiceMailingEnabled = null,
        string? invoiceMailSubjectOverride = null,
        int? autoCloseIdleHours = null)
    {
        if (autoCloseIdleHours.HasValue)
            AutoCloseIdleHours = ClampIdleHours(autoCloseIdleHours.Value);

        if (autoGenerateInvoiceOnResolve.HasValue)
            AutoGenerateInvoiceOnResolve = autoGenerateInvoiceOnResolve.Value;

        if (automatedInvoiceMailingEnabled.HasValue)
            AutomatedInvoiceMailingEnabled = automatedInvoiceMailingEnabled.Value;

        // Null clears the override back to the default subject; an empty string is rejected by
        // the caller rather than silently becoming a blank subject line.
        InvoiceMailSubjectOverride = string.IsNullOrWhiteSpace(invoiceMailSubjectOverride)
            ? null
            : invoiceMailSubjectOverride.Trim()[..Math.Min(200, invoiceMailSubjectOverride.Trim().Length)];
    }

    public void UpdateNotifications(
        bool? notifyAdminOnTicketCreated = null,
        bool? notifyAdminOnTicketReopened = null,
        bool? notifyCustomerOnTicketResolved = null)
    {
        if (notifyAdminOnTicketCreated.HasValue)
            NotifyAdminOnTicketCreated = notifyAdminOnTicketCreated.Value;
        if (notifyAdminOnTicketReopened.HasValue)
            NotifyAdminOnTicketReopened = notifyAdminOnTicketReopened.Value;
        if (notifyCustomerOnTicketResolved.HasValue)
            NotifyCustomerOnTicketResolved = notifyCustomerOnTicketResolved.Value;
    }

    public void SetMaxAttachmentsPerComment(int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), "Attachment limit cannot be negative.");

        MaxAttachmentsPerComment = Math.Min(value, TicketComment.MaxAttachments);
    }

    /// <summary>
    /// Whether resolving this ticket should queue an invoice. Requires both the merchant's
    /// preference and a resolvable financial context — a ticket about "where is my parcel"
    /// has no order behind it and must not produce a zero-value invoice.
    /// </summary>
    public bool ShouldGenerateInvoice(bool hasOrderContext) =>
        AutoGenerateInvoiceOnResolve && hasOrderContext;

    private static int ClampIdleHours(int hours) =>
        Math.Clamp(hours, MinAutoCloseIdleHours, MaxAutoCloseIdleHours);
}