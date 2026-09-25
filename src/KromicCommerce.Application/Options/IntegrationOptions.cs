namespace KromicCommerce.Application.Options;

/// <summary>
/// Application-side view of Razorpay configuration status.
/// Bridged from Infrastructure RazorpayOptions by InfrastructureServiceExtensions.
/// Never contains raw secrets — only status flags and public identifiers.
/// </summary>
public sealed class RazorpayStatusOptions
{
    public bool Enabled { get; set; }
    public string KeyId { get; set; } = string.Empty;
    public bool HasKeySecret { get; set; }
    public bool HasWebhookSecret { get; set; }
    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(KeyId) && HasKeySecret && HasWebhookSecret;
}

public sealed class GoogleOAuthStatusOptions
{
    public bool Enabled { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public bool HasClientSecret { get; set; }
    public string RedirectUri { get; set; } = string.Empty;
    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(ClientId) && HasClientSecret;
}

public sealed class BrevoStatusOptions
{
    public bool Enabled { get; set; }
    public bool HasApiKey { get; set; }
    public string SenderEmail { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public bool IsConfigured => Enabled && HasApiKey;
}

public sealed class SmsStatusOptions
{
    public bool Enabled { get; set; }
    public string Provider { get; set; } = string.Empty;
    public bool HasProviderSettings { get; set; }
    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(Provider);
}

/// <summary>
/// Application-side view of tracking/analytics configuration.
/// Bridged from Infrastructure TrackingOptions by InfrastructureServiceExtensions.
/// Contains only public browser-safe IDs — never secrets.
/// </summary>
public sealed class TrackingPublicOptions
{
    /// <summary>Google Analytics 4 Measurement ID (e.g. G-XXXXXXXXXX). Null when not configured.</summary>
    public string? GoogleAnalyticsMeasurementId { get; set; }

    /// <summary>Meta (Facebook) Pixel ID. Null when not configured.</summary>
    public string? MetaPixelId { get; set; }
}
