namespace KromicCommerce.Infrastructure.Configuration;

/// <summary>
/// Razorpay is CUSTOMER-CONFIGURABLE — not a platform infrastructure secret.
/// Credentials come from environment variables (deployment-scoped per customer instance).
/// No field is [Required] at startup — the application must start even when Razorpay is
/// not configured. Validation happens when a customer attempts to enable/use payments.
/// Never return KeySecret or WebhookSecret through any API.
/// </summary>
public sealed class RazorpayOptions
{
    public const string SectionName = "Razorpay";

    /// <summary>When false, Razorpay payment endpoints return a business error rather than failing at startup.</summary>
    public bool Enabled { get; init; } = false;

    public string KeyId { get; init; } = string.Empty;

    /// <summary>Never log or return this value. Store encrypted if persisting in DB.</summary>
    public string KeySecret { get; init; } = string.Empty;

    /// <summary>Never log or return this value. Store encrypted if persisting in DB.</summary>
    public string WebhookSecret { get; init; } = string.Empty;

    /// <summary>Base URL for Razorpay API. Override for sandbox testing.</summary>
    public string ApiBaseUrl { get; init; } = "https://api.razorpay.com/v1";

    public bool IsConfigured =>
        Enabled &&
        !string.IsNullOrWhiteSpace(KeyId) &&
        !string.IsNullOrWhiteSpace(KeySecret) &&
        !string.IsNullOrWhiteSpace(WebhookSecret);
}
