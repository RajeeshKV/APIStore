namespace KromicCommerce.Contracts.Store;

/// <summary>
/// Authentication flags and OTP policy.
/// </summary>
/// <param name="SmsProvider">
/// Optional, legacy, and ignored for routing decisions. The active SMS gateway is selected via
/// <c>PUT /api/v1/admin/integrations/sms</c>. Omitting this preserves whatever is already stored.
/// </param>
public sealed record UpdateAuthSettingsRequest(
    bool GoogleOAuthEnabled,
    bool EmailPasswordEnabled,
    bool MobileOtpEnabled,
    int OtpExpiryMinutes,
    int OtpResendCooldownSeconds,
    int OtpMaxAttempts,
    string? SmsProvider = null);
