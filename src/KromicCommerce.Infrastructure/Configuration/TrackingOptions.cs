namespace KromicCommerce.Infrastructure.Configuration;

/// <summary>
/// Optional tracking configuration.
/// All fields are nullable — the application starts and operates normally when
/// tracking is not configured. The frontend reads these values from the public
/// store settings endpoint and injects the scripts client-side.
///
/// Secrets: none. All values here are public tracking IDs intended for the browser.
/// Do not add secret keys or server-side provider credentials to this class.
/// </summary>
public sealed class TrackingOptions
{
    public const string SectionName = "Tracking";

    /// <summary>
    /// Shipment tracking provider for order delivery tracking.
    /// Supported values: "Manual" (default), "Delhivery", "Shiprocket".
    /// Optional — defaults to Manual (no external courier API calls).
    /// </summary>
    public string Provider { get; init; } = "Manual";

    /// <summary>Provider-specific credentials and settings keyed by provider name.</summary>
    public Dictionary<string, string> ProviderSettings { get; init; } = [];

    /// <summary>
    /// Google Analytics 4 Measurement ID (e.g. G-XXXXXXXXXX).
    /// Optional — null means Google Analytics is not enabled for this store.
    /// Safe to expose to the frontend via public store settings.
    /// </summary>
    public string? GoogleAnalyticsMeasurementId { get; init; }

    /// <summary>
    /// Meta (Facebook) Pixel ID (numeric string, e.g. "1234567890123456").
    /// Optional — null means Meta Pixel is not enabled for this store.
    /// Safe to expose to the frontend via public store settings.
    /// </summary>
    public string? MetaPixelId { get; init; }
}
