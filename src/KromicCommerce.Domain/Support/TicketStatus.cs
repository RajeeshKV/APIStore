namespace KromicCommerce.Domain.Support;

/// <summary>
/// Ticket lifecycle state.
///
/// The graph is deliberately small and closed:
///
///   Open ──resolve──▶ Resolved ──close──▶ Closed
///     ▲                   │                  │
///     └───── reopen ──────┴───── reopen ─────┘
///
/// Only an admin can move Open → Resolved. Closure may be done by the user
/// (confirming the fix worked) or by the background worker after the idle window.
/// Reopening is available to the user from either terminal state.
/// </summary>
public enum TicketStatus
{
    /// <summary>Awaiting a first admin response, or reopened and awaiting further work.</summary>
    Open = 0,

    /// <summary>An admin believes the issue is handled. The invoice trigger fires here.</summary>
    Resolved = 1,

    /// <summary>Terminal until reopened. Set by the user or by the idle auto-close worker.</summary>
    Closed = 2
}