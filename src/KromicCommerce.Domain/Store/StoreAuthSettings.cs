namespace KromicCommerce.Domain.Store;

/// <summary>
/// Authentication options for this store deployment.
/// Controls which auth methods customers may use.
///
/// Google OAuth credentials (ClientId, EncryptedClientSecret, RedirectUri) are stored
/// here so they persist in the database alongside other business settings.
/// The ClientSecret is stored encrypted — the domain never sees plaintext secrets.
/// </summary>
public sealed class StoreAuthSettings : ValueObject
{
    public static StoreAuthSettings Default() => new()
    {
        GoogleOAuthEnabled = false,
        EmailPasswordEnabled = true,
        MobileOtpEnabled = false,
        OtpExpiryMinutes = 10,
        OtpResendCooldownSeconds = 60,
        OtpMaxAttempts = 5,
        SmsProvider = "Fast2SMS",
        GoogleClientId = null,
        EncryptedGoogleClientSecret = null,
        GoogleRedirectUri = null
    };

    public bool GoogleOAuthEnabled { get; private set; }
    public bool EmailPasswordEnabled { get; private set; }
    public bool MobileOtpEnabled { get; private set; }

    /// <summary>How long an OTP is valid after sending. Default: 10 minutes.</summary>
    public int OtpExpiryMinutes { get; private set; }

    /// <summary>Minimum seconds a customer must wait before requesting a new OTP. Default: 60.</summary>
    public int OtpResendCooldownSeconds { get; private set; }

    /// <summary>Maximum verification attempts before an OTP is invalidated. Default: 5.</summary>
    public int OtpMaxAttempts { get; private set; }

    /// <summary>Active SMS provider name key. E.g. "Fast2SMS", "Twilio".</summary>
    public string SmsProvider { get; private set; } = "Fast2SMS";

    // -----------------------------------------------------------------------
    // Google OAuth credentials — stored in DB, not environment variables.
    // ClientSecret is encrypted before storage; never returned by APIs.
    // -----------------------------------------------------------------------

    /// <summary>Google OAuth 2.0 Client ID (public identifier, safe to display masked).</summary>
    public string? GoogleClientId { get; private set; }

    /// <summary>
    /// Google OAuth 2.0 Client Secret encrypted via ISecretProtectionService.
    /// Never return this value through any API. Decrypt only at the point of use.
    /// </summary>
    public string? EncryptedGoogleClientSecret { get; private set; }

    /// <summary>
    /// The OAuth redirect URI registered in Google Cloud Console.
    /// Must exactly match the value configured there.
    /// </summary>
    public string? GoogleRedirectUri { get; private set; }

    /// <summary>
    /// True when all required Google OAuth credentials are present in the database.
    /// Does not imply the credentials are valid — only that they have been configured.
    /// </summary>
    public bool IsGoogleOAuthConfigured =>
        !string.IsNullOrWhiteSpace(GoogleClientId) &&
        !string.IsNullOrWhiteSpace(EncryptedGoogleClientSecret);

    // -----------------------------------------------------------------------
    // Factory — used by UpdateAuthSettingsCommand (feature flags only)
    // -----------------------------------------------------------------------

    public static StoreAuthSettings Create(
        bool googleOAuthEnabled,
        bool emailPasswordEnabled,
        bool mobileOtpEnabled,
        int otpExpiryMinutes,
        int otpResendCooldownSeconds,
        int otpMaxAttempts,
        string smsProvider,
        // Carry existing credentials through unchanged when only flags change
        string? googleClientId = null,
        string? encryptedGoogleClientSecret = null,
        string? googleRedirectUri = null)
    {
        if (otpExpiryMinutes < 1)
            throw new ArgumentException("OTP expiry must be at least 1 minute.", nameof(otpExpiryMinutes));
        if (otpResendCooldownSeconds < 0)
            throw new ArgumentException("OTP resend cooldown must be >= 0.", nameof(otpResendCooldownSeconds));
        if (otpMaxAttempts < 1)
            throw new ArgumentException("OTP max attempts must be at least 1.", nameof(otpMaxAttempts));
        if (string.IsNullOrWhiteSpace(smsProvider))
            throw new ArgumentException("SMS provider must not be empty.", nameof(smsProvider));

        return new StoreAuthSettings
        {
            GoogleOAuthEnabled = googleOAuthEnabled,
            EmailPasswordEnabled = emailPasswordEnabled,
            MobileOtpEnabled = mobileOtpEnabled,
            OtpExpiryMinutes = otpExpiryMinutes,
            OtpResendCooldownSeconds = otpResendCooldownSeconds,
            OtpMaxAttempts = otpMaxAttempts,
            SmsProvider = smsProvider.Trim(),
            GoogleClientId = googleClientId,
            EncryptedGoogleClientSecret = encryptedGoogleClientSecret,
            GoogleRedirectUri = googleRedirectUri
        };
    }

    /// <summary>Creates a new instance with updated Google credentials, preserving all other fields.</summary>
    internal StoreAuthSettings WithGoogleCredentials(
        string clientId,
        string encryptedClientSecret,
        string? redirectUri)
        => new()
        {
            GoogleOAuthEnabled = GoogleOAuthEnabled,
            EmailPasswordEnabled = EmailPasswordEnabled,
            MobileOtpEnabled = MobileOtpEnabled,
            OtpExpiryMinutes = OtpExpiryMinutes,
            OtpResendCooldownSeconds = OtpResendCooldownSeconds,
            OtpMaxAttempts = OtpMaxAttempts,
            SmsProvider = SmsProvider,
            GoogleClientId = clientId.Trim(),
            EncryptedGoogleClientSecret = encryptedClientSecret,
            GoogleRedirectUri = redirectUri?.Trim()
        };

    /// <summary>Creates a new instance with Google credentials cleared, preserving all other fields.</summary>
    internal StoreAuthSettings WithoutGoogleCredentials()
        => new()
        {
            GoogleOAuthEnabled = false, // disable when credentials are cleared
            EmailPasswordEnabled = EmailPasswordEnabled,
            MobileOtpEnabled = MobileOtpEnabled,
            OtpExpiryMinutes = OtpExpiryMinutes,
            OtpResendCooldownSeconds = OtpResendCooldownSeconds,
            OtpMaxAttempts = OtpMaxAttempts,
            SmsProvider = SmsProvider,
            GoogleClientId = null,
            EncryptedGoogleClientSecret = null,
            GoogleRedirectUri = null
        };

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return GoogleOAuthEnabled;
        yield return EmailPasswordEnabled;
        yield return MobileOtpEnabled;
        yield return OtpExpiryMinutes;
        yield return OtpResendCooldownSeconds;
        yield return OtpMaxAttempts;
        yield return SmsProvider;
        yield return GoogleClientId;
        yield return EncryptedGoogleClientSecret;
        yield return GoogleRedirectUri;
    }
}
