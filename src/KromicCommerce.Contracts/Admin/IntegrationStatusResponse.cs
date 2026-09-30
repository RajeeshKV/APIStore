namespace KromicCommerce.Contracts.Admin;

/// <summary>
/// Returns integration configuration status to Admin.
/// NEVER includes raw secrets, API keys, or credentials.
/// Only masked identifiers and configuration state are safe to return.
/// </summary>
public sealed record IntegrationStatusResponse(
    string IntegrationName,
    bool Enabled,
    bool IsConfigured,

    /// <summary>Masked public identifier — e.g. "rzp_****1234". Never the full key.</summary>
    string? MaskedKeyId,

    /// <summary>True if a secret has been configured; does not reveal the secret.</summary>
    bool HasSecret,

    /// <summary>Additional non-sensitive display fields (e.g. sender email for Brevo).</summary>
    Dictionary<string, string>? PublicFields = null);

public sealed record UpdateRazorpayConfigRequest(
    bool Enabled,
    string KeyId,
    /// <summary>Raw plaintext — encrypted before storage. Never logged or returned.</summary>
    string KeySecret,
    string WebhookSecret);

// UpdateCashOnDeliveryRequest was removed with the duplicate COD endpoint. COD is configured
// through UpdateDeliverySettingsRequest (Admin → Shipping), which carries CodEnabled and
// CodExtraFee together. See UpdateDeliverySettingsRequest in Contracts/Store.

public sealed record UpdateGoogleOAuthConfigRequest(
    bool Enabled,
    string ClientId,
    /// <summary>Raw plaintext — encrypted before storage. Never logged or returned.</summary>
    string ClientSecret);

public sealed record UpdateSmsConfigRequest(
    bool Enabled,
    string Provider,
    Dictionary<string, string>? ProviderSettings = null);

public sealed record UpdateEmailConfigRequest(
    bool Enabled,
    string Mode,         // "KromicManaged" | "CustomerBrevo"
    string SenderName,
    string? SenderEmail,
    /// <summary>Brevo API key — raw plaintext encrypted before storage. Never returned.</summary>
    string? ApiKey = null);
