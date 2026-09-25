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
}
