namespace KromicCommerce.Application.Abstractions.Auth;

/// <summary>
/// OTP generation and verification helpers.
/// Business logic (attempt tracking, expiry, purpose enforcement) lives in the command handlers.
/// </summary>
public interface IOtpService
{
    /// <summary>Generates a cryptographically random numeric OTP string of the given length.</summary>
    string GenerateOtp(int length = 6);

    /// <summary>Returns the SHA-256 hash of the given OTP. Never log the raw OTP.</summary>
    string HashOtp(string otp);

    /// <summary>Returns true if the hash of the submitted OTP matches the stored hash.</summary>
    bool VerifyOtp(string submittedOtp, string storedHash);
}
