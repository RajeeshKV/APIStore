namespace KromicCommerce.Domain.Webhooks;

/// <summary>
/// Persisted record of a processed webhook event — idempotency key.
/// Before processing a webhook, check whether its ProviderEventId has already been seen.
/// If it has, return success without re-processing (idempotent).
/// Prevents duplicate state changes from retried or replayed webhooks.
/// </summary>
public sealed class WebhookEvent : Entity
{
    private WebhookEvent() { } // EF constructor

    public static WebhookEvent Create(
        string provider,
        string providerEventId,
        string eventType,
        string payload)
    {
        if (string.IsNullOrWhiteSpace(provider))         throw new ArgumentException("Provider required.", nameof(provider));
        if (string.IsNullOrWhiteSpace(providerEventId))  throw new ArgumentException("Provider event ID required.", nameof(providerEventId));
        if (string.IsNullOrWhiteSpace(eventType))        throw new ArgumentException("Event type required.", nameof(eventType));

        return new WebhookEvent
        {
            Provider = provider,
            ProviderEventId = providerEventId,
            EventType = eventType,
            Payload = payload,
            ReceivedAt = DateTime.UtcNow
        };
    }

    public string Provider { get; private set; } = string.Empty;

    /// <summary>Stable identifier from the provider (e.g. Razorpay event ID). Used as idempotency key.</summary>
    public string ProviderEventId { get; private set; } = string.Empty;

    public string EventType { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public DateTime ReceivedAt { get; private set; }
    public DateTime? ProcessedAt { get; private set; }

    public bool IsProcessed => ProcessedAt is not null;

    public void MarkProcessed() => ProcessedAt = DateTime.UtcNow;
}
