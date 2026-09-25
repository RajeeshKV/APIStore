namespace KromicCommerce.Infrastructure.Configuration;

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimit";

    /// <summary>Window duration in seconds for the general API rate limit.</summary>
    public int WindowSeconds { get; init; } = 60;

    /// <summary>Maximum requests per window for general API endpoints.</summary>
    public int PermitLimit { get; init; } = 100;

    /// <summary>Maximum queue depth when the limit is exceeded (0 = reject immediately).</summary>
    public int QueueLimit { get; init; } = 0;

    // Sensitive endpoint limits — applied individually to OTP, auth, password reset etc.
    public int AuthWindowSeconds { get; init; } = 60;
    public int AuthPermitLimit { get; init; } = 10;

    public int OtpWindowSeconds { get; init; } = 60;
    public int OtpPermitLimit { get; init; } = 5;
}
