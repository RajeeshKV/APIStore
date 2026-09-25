namespace KromicCommerce.Domain.Outbox;

/// <summary>
/// Reliable asynchronous side-effect delivery via the Outbox pattern.
/// An outbox event is created inside the same transaction as the business operation.
/// A background processor polls and dispatches pending events (email, SMS, analytics, etc.).
/// Failures are retried up to MaxRetries; after that the event is marked permanently failed.
/// The application never fails a core transaction because an outbox consumer is unavailable.
/// </summary>
public sealed class OutboxEvent : Entity
{
    private OutboxEvent() { } // EF constructor

    public static OutboxEvent Create(string type, string payload)
    {
        if (string.IsNullOrWhiteSpace(type))
            throw new ArgumentException("Event type is required.", nameof(type));
        if (string.IsNullOrWhiteSpace(payload))
            throw new ArgumentException("Event payload is required.", nameof(payload));

        return new OutboxEvent
        {
            EventType = type,
            Payload = payload,
            CreatedAt = DateTime.UtcNow,
            RetryCount = 0
        };
    }

    public string EventType { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }
    public DateTime? ProcessedAt { get; private set; }
    public int RetryCount { get; private set; }
    public string? Error { get; private set; }

    public bool IsProcessed => ProcessedAt is not null;
    public bool HasFailed(int maxRetries) => !IsProcessed && RetryCount >= maxRetries;

    // -----------------------------------------------------------------------
    // Behaviour
    // -----------------------------------------------------------------------

    public void MarkProcessed() => ProcessedAt = DateTime.UtcNow;

    public void RecordFailure(string errorMessage)
    {
        RetryCount++;
        Error = errorMessage;
    }
}
