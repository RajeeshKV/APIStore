namespace KromicCommerce.Infrastructure.Configuration;

/// <summary>
/// SMS is OPTIONAL — the application must start and serve requests without SMS configured.
/// If SMS is not configured, OTP operations that require SMS fail with a business error;
/// other auth methods continue working.
/// Provider credentials must never be returned by any API.
/// </summary>
public sealed class SmsOptions
{
    public const string SectionName = "Sms";

    public bool Enabled { get; init; } = false;

    /// <summary>Provider name: "Fast2SMS", "Twilio", etc.</summary>
    public string Provider { get; init; } = string.Empty;

    /// <summary>Provider-specific key-value settings (e.g. ApiKey, SenderId).</summary>
    public Dictionary<string, string> ProviderSettings { get; init; } = [];

    public bool IsConfigured =>
        Enabled && !string.IsNullOrWhiteSpace(Provider);
}
