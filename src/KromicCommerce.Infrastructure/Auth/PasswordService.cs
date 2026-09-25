using System.Security.Cryptography;
using KromicCommerce.Application.Abstractions.Auth;
using Microsoft.AspNetCore.Identity;

namespace KromicCommerce.Infrastructure.Auth;

/// <summary>
/// Password hashing uses ASP.NET Core PasswordHasher (PBKDF2 / bcrypt-style, slow by design).
/// Token hashing uses SHA-256 (fast, appropriate for already-random high-entropy values).
/// Reset tokens are 32-byte random values encoded as URL-safe base64.
///
/// Never log raw tokens or password hashes.
/// </summary>
internal sealed class PasswordService : IPasswordService
{
    private static readonly PasswordHasher<object> Hasher = new();

    public string Hash(string password) =>
        Hasher.HashPassword(new object(), password);

    public bool Verify(string password, string hash) =>
        Hasher.VerifyHashedPassword(new object(), hash, password)
            != PasswordVerificationResult.Failed;

    public string HashToken(string rawToken)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(rawToken);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public string GenerateResetToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        // URL-safe base64 — no padding, no + or /
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
