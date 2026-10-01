namespace KromicCommerce.Application.Options;

/// <summary>
/// Public application URL configuration bridged from AppOptions (Infrastructure).
/// Used by Application-layer handlers to generate deterministic callback/webhook URLs
/// without taking a direct dependency on Infrastructure.Configuration.
/// </summary>
public sealed class AppPublicOptions
{
    /// <summary>
    /// The public-facing base URL of this backend deployment.
    /// E.g. "https://backend.kromic.in" — no trailing slash.
    /// Used to generate OAuth redirect URIs and webhook URLs shown to the admin.
    /// </summary>
    public string ApiBaseUrl { get; set; } = string.Empty;

    /// <summary>Computed Google OAuth redirect URI the admin must register in Google Cloud Console.</summary>
    public string GoogleRedirectUri =>
        string.IsNullOrWhiteSpace(ApiBaseUrl)
            ? string.Empty
            : $"{ApiBaseUrl.TrimEnd('/')}/api/v1/auth/google/callback";

    /// <summary>Computed Razorpay webhook URL the admin must register in the Razorpay Dashboard.</summary>
    public string RazorpayWebhookUrl =>
        string.IsNullOrWhiteSpace(ApiBaseUrl)
            ? string.Empty
            : $"{ApiBaseUrl.TrimEnd('/')}/api/v1/webhooks/payment";

    /// <summary>
    /// Frontend public URL for generating customer-facing links in emails.
    /// Bridged from App__FrontendUrl.
    /// </summary>
    public string? FrontendUrl { get; set; }
}

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
