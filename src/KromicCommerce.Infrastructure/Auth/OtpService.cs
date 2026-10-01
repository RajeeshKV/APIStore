using System.Security.Cryptography;
using System.Text;
using KromicCommerce.Application.Abstractions.Auth;
using KromicCommerce.Application.Options;

namespace KromicCommerce.Infrastructure.Auth;

internal sealed class OtpService : IOtpService
{
    public string GenerateOtp(int length = SmsOtpDefaults.Length)
    {
        if (length is < SmsOtpDefaults.MinLength or > SmsOtpDefaults.MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(length),
                $"OTP length must be between {SmsOtpDefaults.MinLength} and {SmsOtpDefaults.MaxLength}.");
        }

        // One cryptographically secure draw per digit. This stays uniform for every supported
        // length, unlike GetInt32(0, (int)Math.Pow(10, length)) which overflows past 9 digits.
        return string.Create(length, 0, static (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = (char)('0' + RandomNumberGenerator.GetInt32(0, 10));
        });
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
