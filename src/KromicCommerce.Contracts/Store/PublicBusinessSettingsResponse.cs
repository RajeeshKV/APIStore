namespace KromicCommerce.Contracts.Store;

/// <summary>
/// Public-facing store settings. Excludes all admin-only fields (email config, auth settings, etc.).
/// Returned unauthenticated — never expose secrets, API keys, or internal config here.
/// Nullable fields: frontend must hide corresponding UI elements when null/empty.
/// </summary>
public sealed record PublicBusinessSettingsResponse(
    string BusinessName,
    string? LegalName,
    string? WebsiteUrl,
    string? SupportEmail,
    string? SupportPhone,
    string? LogoUrl,
    string? Address,
    string CountryCode,
    string CurrencyCode,
    string TimeZoneId,
    string Culture,
    bool IsStoreOpen,
    string? TemporaryClosureMessage,
    // Social links — null when not configured; frontend should hide the corresponding button/link
    string? FacebookUrl,
    string? InstagramUrl,
    string? TwitterUrl,
    string? YoutubeUrl,
    string? WhatsAppNumber,
    string? LinkedInUrl,
    SeoSettingsDto Seo,
    DeliverySettingsDto Delivery,
    /// <summary>
    /// Public analytics tracking IDs for client-side script injection.
    /// Null fields mean that integration is not configured for this store.
    /// These are browser-safe public IDs — not secrets.
    /// </summary>
    TrackingSettingsDto Tracking,
    /// <summary>
    /// Authentication methods available on this storefront.
    /// Used by the login page to show/hide auth options.
    /// GoogleClientId is included only when Google OAuth is enabled — it is a public
    /// identifier safe to expose to the browser (not a secret).
    /// </summary>
    PublicAuthSettingsDto Auth);

/// <summary>Public tracking configuration for client-side analytics integration.</summary>
public sealed record TrackingSettingsDto(
    string? GoogleAnalyticsMeasurementId,
    string? MetaPixelId);

/// <summary>
/// Storefront-facing authentication configuration.
/// Contains only what the login page needs — no secrets, no OTP internals.
/// </summary>
public sealed record PublicAuthSettingsDto(
    bool GoogleOAuthEnabled,
    /// <summary>
    /// Google OAuth 2.0 Client ID. Present only when GoogleOAuthEnabled = true.
    /// This is a public identifier — safe to embed in the browser for the Google Sign-In SDK.
    /// Never expose the Client Secret here.
    /// </summary>
    string? GoogleClientId,
    bool EmailPasswordEnabled,
    bool MobileOtpEnabled);
