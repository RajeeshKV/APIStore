namespace KromicCommerce.Domain.Support;

/// <summary>Lifecycle of a single invoice revision.</summary>
public enum TicketInvoiceStatus
{
    /// <summary>Queued for the background renderer. Content snapshot is already frozen.</summary>
    Pending = 0,

    /// <summary>PDF rendered and stored. Terminal until a new revision supersedes it.</summary>
    Generated = 1,

    /// <summary>Rendering failed. Retried up to <see cref="TicketInvoice.MaxGenerationAttempts"/>.</summary>
    Failed = 2,

    /// <summary>Replaced by a newer revision. Kept for the audit trail.</summary>
    Superseded = 3
}