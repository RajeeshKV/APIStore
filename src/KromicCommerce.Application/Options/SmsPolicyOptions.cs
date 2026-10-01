namespace KromicCommerce.Application.Options;

/// <summary>
/// Application-side view of SMS policy, bridged from Infrastructure configuration.
/// Carries policy values only — never provider credentials.
/// </summary>
public sealed class SmsPolicyOptions
{
    /// <summary>How long a code stays valid.</summary>
    public int ExpiryMinutes { get; set; } = 10;

    /// <summary>Minimum seconds between two sends to the same number and purpose.</summary>
    public int ResendCooldownSeconds { get; set; } = 60;

    /// <summary>Failed verification attempts before a code is burned.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Digits in the code. Four is the store's default.</summary>
    public int Length { get; set; } = SmsOtpDefaults.Length;

    /// <summary>
    /// When true, checkout rejects customers whose phone is not verified. Only has an effect
    /// while a provider is actually configured, because verification needs delivery.
    /// </summary>
    public bool RequireVerifiedPhoneAtCheckout { get; set; }
}
