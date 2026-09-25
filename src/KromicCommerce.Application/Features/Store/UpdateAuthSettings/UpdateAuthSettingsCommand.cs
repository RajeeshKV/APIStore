namespace KromicCommerce.Application.Features.Store.UpdateAuthSettings;

public sealed record UpdateAuthSettingsCommand(
    bool GoogleOAuthEnabled,
    bool EmailPasswordEnabled,
    bool MobileOtpEnabled,
    int OtpExpiryMinutes,
    int OtpResendCooldownSeconds,
    int OtpMaxAttempts,
    string SmsProvider) : ICommand;
