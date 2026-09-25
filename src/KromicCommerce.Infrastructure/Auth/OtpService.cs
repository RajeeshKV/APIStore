using System.Security.Cryptography;
using System.Text;
using KromicCommerce.Application.Abstractions.Auth;

namespace KromicCommerce.Infrastructure.Auth;

internal sealed class OtpService : IOtpService
{
    public string GenerateOtp(int length = 6)
    {
        if (length is < 4 or > 8)
            throw new ArgumentOutOfRangeException(nameof(length), "OTP length must be between 4 and 8.");

        // Cryptographically secure uniform random digits
        var max = (int)Math.Pow(10, length);
        var otp = RandomNumberGenerator.GetInt32(0, max);
        return otp.ToString().PadLeft(length, '0');
    }

    public string HashOtp(string otp)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(otp));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public bool VerifyOtp(string submittedOtp, string storedHash)
    {
        var submittedHash = HashOtp(submittedOtp);
        // Constant-time comparison prevents timing attacks
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(submittedHash),
            Encoding.UTF8.GetBytes(storedHash));
    }
}
