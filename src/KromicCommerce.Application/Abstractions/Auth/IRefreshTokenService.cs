namespace KromicCommerce.Application.Abstractions.Auth;

/// <summary>
/// Manages refresh token lifecycle: generation, rotation, reuse detection, revocation.
/// Raw tokens are never persisted — only their SHA-256 hash.
/// </summary>
public interface IRefreshTokenService
{
    /// <summary>Generates a cryptographically random raw token string.</summary>
    string GenerateRawToken();

    /// <summary>Returns the SHA-256 hash of the given raw token.</summary>
    string HashToken(string rawToken);
}
