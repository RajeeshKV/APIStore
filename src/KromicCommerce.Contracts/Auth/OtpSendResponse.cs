namespace KromicCommerce.Contracts.Auth;

public sealed record OtpSendResponse(
    /// <summary>When the OTP expires (UTC). Shown to user so they know how long they have.</summary>
    DateTime ExpiresAtUtc,
    /// <summary>Earliest time to resend (UTC). Prevents spam.</summary>
    DateTime ResendAvailableAtUtc);
