using KromicCommerce.Application.Options;

namespace KromicCommerce.Infrastructure.Configuration;

/// <summary>
/// OTP policy. Applies to every <c>OtpPurpose</c> (verification, login, password reset).
/// </summary>
public sealed class SmsOtpPolicyOptions
{
    /// <summary>How long a code stays valid. 1–60 minutes.</summary>
    public int ExpiryMinutes { get; init; } = SmsOtpDefaults.ExpiryMinutes;

    /// <summary>Minimum seconds between two sends to the same number and purpose.</summary>
    public int ResendCooldownSeconds { get; init; } = SmsOtpDefaults.ResendCooldownSeconds;

    /// <summary>Failed verification attempts before a code is burned.</summary>
    public int MaxAttempts { get; init; } = SmsOtpDefaults.MaxAttempts;

    /// <summary>Digits in the code. <see cref="SmsOtpDefaults.Length"/> by default.</summary>
    public int Length { get; init; } = SmsOtpDefaults.Length;

    /// <summary>Ceilings enforced at send time so a bad config cannot weaken the policy.</summary>
    public int ClampedExpiryMinutes => Math.Clamp(ExpiryMinutes, 1, 60);

    public int ClampedResendCooldownSeconds => Math.Max(ResendCooldownSeconds, 0);

    public int ClampedMaxAttempts => Math.Clamp(MaxAttempts, 1, 10);

    public int ClampedLength => Math.Clamp(Length, SmsOtpDefaults.MinLength, SmsOtpDefaults.MaxLength);
}
