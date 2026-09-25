using System.Security.Cryptography;
using System.Text;
using KromicCommerce.Application.Abstractions.Auth;

namespace KromicCommerce.Infrastructure.Auth;

internal sealed class RefreshTokenService : IRefreshTokenService
{
    public string GenerateRawToken()
    {
        // 64 bytes → 512 bits of entropy, URL-safe Base64
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public string HashToken(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
