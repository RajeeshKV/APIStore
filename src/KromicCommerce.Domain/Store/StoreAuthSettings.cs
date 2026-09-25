namespace KromicCommerce.Domain.Store;

/// <summary>
/// Authentication options for this store deployment.
/// Controls which auth methods customers may use.
/// Provider credentials (Google ClientId/Secret, SMS API keys) stay in environment variables,
/// not here — this entity only controls business-level feature flags and UX config.
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
        SmsProvider = "Fast2SMS"
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

    /// <summary>
    /// Active SMS provider name key. Must match a configured provider adapter.
    /// E.g. "Fast2SMS", "Twilio".
    /// </summary>
    public string SmsProvider { get; private set; } = "Fast2SMS";

    // -----------------------------------------------------------------------
    // Factory / update
    // -----------------------------------------------------------------------

    public static StoreAuthSettings Create(
        bool googleOAuthEnabled,
        bool emailPasswordEnabled,
        bool mobileOtpEnabled,
        int otpExpiryMinutes,
        int otpResendCooldownSeconds,
        int otpMaxAttempts,
        string smsProvider)
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
            SmsProvider = smsProvider.Trim()
        };
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return GoogleOAuthEnabled;
        yield return EmailPasswordEnabled;
        yield return MobileOtpEnabled;
        yield return OtpExpiryMinutes;
        yield return OtpResendCooldownSeconds;
        yield return OtpMaxAttempts;
        yield return SmsProvider;
    }
}
