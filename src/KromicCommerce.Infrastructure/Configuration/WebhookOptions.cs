namespace KromicCommerce.Infrastructure.Configuration;

public sealed class WebhookOptions
{
    public const string SectionName = "Webhook";

    /// <summary>Maximum age in minutes for an incoming webhook timestamp to be accepted. Default: 5.</summary>
    public int ToleranceMinutes { get; init; } = 5;

    /// <summary>Idempotency window in hours — duplicate webhook events within this window are discarded. Default: 24.</summary>
    public int IdempotencyWindowHours { get; init; } = 24;
}
