namespace KromicCommerce.Domain.Support;

/// <summary>
/// Who caused a status transition. Recorded on every history row so an
/// auto-close is always distinguishable from a user-confirmed close.
/// </summary>
public enum TicketTransitionActor
{
    /// <summary>The customer who opened the ticket.</summary>
    User = 0,

    /// <summary>An authenticated administrator.</summary>
    Admin = 1,

    /// <summary>A background worker — currently only the idle auto-close.</summary>
    System = 2
}