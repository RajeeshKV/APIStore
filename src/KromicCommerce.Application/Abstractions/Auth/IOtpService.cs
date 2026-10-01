namespace KromicCommerce.Application.Abstractions.Auth;

using KromicCommerce.Application.Options;

/// <summary>
/// OTP generation and verification helpers.
/// Business logic (attempt tracking, expiry, purpose enforcement) lives in the command handlers.
/// </summary>
public interface IOtpService
{
    /// <summary>
    /// Generates a cryptographically random numeric OTP string of the given length.
    /// The default is declared here and must match the implementation's — a caller binding
    /// through this interface uses <em>this</em> default, not the concrete class's.
    /// </summary>
    string GenerateOtp(int length = SmsOtpDefaults.Length);

    /// <summary>Returns the SHA-256 hash of the given OTP. Never log the raw OTP.</summary>
    string HashOtp(string otp);

    /// <summary>Returns true if the hash of the submitted OTP matches the stored hash.</summary>
    bool VerifyOtp(string submittedOtp, string storedHash);
}
