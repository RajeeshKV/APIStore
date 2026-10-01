namespace KromicCommerce.Application.Options;

/// <summary>
/// The store's OTP policy defaults, in one place so the Infrastructure configuration defaults
/// and the Application policy bridge cannot disagree.
/// </summary>
public static class SmsOtpDefaults
{
    /// <summary>How long a code stays valid after sending.</summary>
    public const int ExpiryMinutes = 10;

    /// <summary>Minimum seconds between two sends to the same number and purpose.</summary>
    public const int ResendCooldownSeconds = 60;

    /// <summary>Failed verification attempts before a code is burned.</summary>
    public const int MaxAttempts = 5;

    /// <summary>Digits in a generated code.</summary>
    public const int Length = 4;

    /// <summary>Shortest code accepted. Four digits is the floor; shorter is guessable.</summary>
    public const int MinLength = 4;

    /// <summary>Longest code generated.</summary>
    public const int MaxLength = 10;
}
