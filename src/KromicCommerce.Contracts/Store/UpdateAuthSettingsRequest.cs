namespace KromicCommerce.Contracts.Store;

public sealed record UpdateAuthSettingsRequest(
    bool GoogleOAuthEnabled,
    bool EmailPasswordEnabled,
    bool MobileOtpEnabled,
    int OtpExpiryMinutes,
    int OtpResendCooldownSeconds,
    int OtpMaxAttempts,
    string SmsProvider);
