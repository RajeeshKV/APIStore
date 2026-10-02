namespace KromicCommerce.Application.Features.Store.UpdateAuthSettings;

/// <summary>
/// Updates the authentication flags and OTP policy.
/// </summary>
/// <param name="SmsProvider">
/// Optional and legacy. The active SMS gateway is selected through
/// <c>PUT /api/v1/admin/integrations/sms</c>, which owns the authoritative provider row; this
/// field is kept only so older admin screens keep round-tripping harmlessly. Omit it and the
/// stored value is preserved.
/// </param>
public sealed record UpdateAuthSettingsCommand(
    bool GoogleOAuthEnabled,
    bool EmailPasswordEnabled,
    bool MobileOtpEnabled,
    int OtpExpiryMinutes,
    int OtpResendCooldownSeconds,
    int OtpMaxAttempts,
    string? SmsProvider = null) : ICommand<AdminBusinessSettingsResponse>;
