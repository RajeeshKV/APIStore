namespace KromicCommerce.Infrastructure.Configuration;

public sealed class BackgroundWorkerOptions
{
    public const string SectionName = "BackgroundWorker";

    /// <summary>Outbox polling interval in seconds. Default: 15.</summary>
    public int OutboxIntervalSeconds { get; init; } = 15;

    /// <summary>Maximum outbox events processed per batch. Default: 50.</summary>
    public int OutboxBatchSize { get; init; } = 50;

    /// <summary>Maximum retry attempts for a failed outbox event before it is marked dead. Default: 5.</summary>
    public int OutboxMaxRetries { get; init; } = 5;

    /// <summary>Email queue polling interval in seconds. Default: 30.</summary>
    public int EmailIntervalSeconds { get; init; } = 30;

    /// <summary>Maximum emails processed per batch. Default: 20.</summary>
    public int EmailBatchSize { get; init; } = 20;

    /// <summary>
    /// Ticket idle-close polling interval in seconds. Default: 300.
    /// The deadline itself is 72 hours by default (SupportSettings.AutoCloseIdleHours); this
    /// only controls how promptly a due ticket is noticed afterwards, so a five-minute cadence
    /// costs almost nothing and bounds how late a closure can be.
    /// </summary>
    public int TicketAutoCloseIntervalSeconds { get; init; } = 300;

    /// <summary>
    /// Invoice render polling interval in seconds. Default: 15.
    /// Shorter than the auto-close interval because an administrator who just resolved a ticket
    /// expects the invoice to appear, and the edit-before-generation window is only useful if
    /// the render does not lag far behind the queue.
    /// </summary>
    public int TicketInvoiceIntervalSeconds { get; init; } = 15;
}
