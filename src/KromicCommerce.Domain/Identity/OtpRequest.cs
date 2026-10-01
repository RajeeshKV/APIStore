namespace KromicCommerce.Domain.Identity;

/// <summary>
/// Represents a single OTP send request.
/// Raw OTP is never stored — only the SHA-256 hash.
/// Attempt tracking and expiry prevent brute force.
/// </summary>
public sealed class OtpRequest : Entity
{
    private OtpRequest() { } // EF constructor

    /// <summary>Default attempt ceiling when the caller does not supply one.</summary>
    public const int DefaultMaxAttempts = 5;

    public static OtpRequest Create(
        string phoneNumber,
        string otpHash,
        OtpPurpose purpose,
        DateTime expiresAt,
        Guid? userId = null,
        int maxAttempts = DefaultMaxAttempts)
    {
        if (maxAttempts < 1)
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "maxAttempts must be at least 1.");

        return new()
        {
            PhoneNumber = phoneNumber.Trim(),
            OtpHash = otpHash,
            Purpose = purpose,
            ExpiresAt = expiresAt,
            CreatedAt = DateTime.UtcNow,
            UserId = userId,
            Attempts = 0,
            MaxAttempts = maxAttempts
        };
    }

    public string PhoneNumber { get; private set; } = string.Empty;

    /// <summary>SHA-256 hash of the raw OTP. Never log or store the raw OTP.</summary>
    public string OtpHash { get; private set; } = string.Empty;

    public OtpPurpose Purpose { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime? VerifiedAt { get; private set; }

    /// <summary>Number of verification attempts made against this request.</summary>
    public int Attempts { get; private set; }

    /// <summary>
    /// Attempt ceiling captured when the code was issued, so a later configuration change
    /// cannot retroactively loosen the limit on codes already in flight.
    /// </summary>
    public int MaxAttempts { get; private set; } = DefaultMaxAttempts;

    /// <summary>Associated user — null for pre-registration phone verification.</summary>
    public Guid? UserId { get; private set; }

    // -----------------------------------------------------------------------
    // Behaviour
    // -----------------------------------------------------------------------

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool IsVerified => VerifiedAt is not null;
    public bool IsExhausted => Attempts >= MaxAttempts;

    public bool CanAttempt() => !IsExpired && !IsVerified && !IsExhausted;

    /// <summary>Increment attempt counter. Returns true if max attempts reached after increment.</summary>
    public bool RecordAttempt()
    {
        Attempts++;
        return IsExhausted;
    }

    public void MarkVerified() => VerifiedAt = DateTime.UtcNow;
}

public enum OtpPurpose
{
    PhoneVerification,
    Login,
    PasswordReset
}
