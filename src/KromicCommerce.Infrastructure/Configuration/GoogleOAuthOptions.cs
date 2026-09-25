namespace KromicCommerce.Infrastructure.Configuration;

/// <summary>
/// Google OAuth is CUSTOMER-CONFIGURABLE — each store may use its own Google Cloud project.
/// No field is [Required] at startup — application starts without Google OAuth configured.
/// Validation happens when a customer tries to initiate a Google login.
/// ClientSecret must never be logged or returned by any API.
/// </summary>
public sealed class GoogleOAuthOptions
{
    public const string SectionName = "GoogleOAuth";

    public bool Enabled { get; init; } = false;

    public string ClientId { get; init; } = string.Empty;

    /// <summary>Never log or return this value.</summary>
    public string ClientSecret { get; init; } = string.Empty;

    /// <summary>OAuth redirect URI registered in Google Cloud Console.</summary>
    public string RedirectUri { get; init; } = string.Empty;

    public bool IsConfigured =>
        Enabled &&
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(ClientSecret) &&
        !string.IsNullOrWhiteSpace(RedirectUri);
}
