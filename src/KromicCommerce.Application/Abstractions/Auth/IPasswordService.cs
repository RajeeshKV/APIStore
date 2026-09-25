namespace KromicCommerce.Application.Abstractions.Auth;

/// <summary>
/// Password hashing, verification, and high-entropy token hashing.
///
/// Passwords use ASP.NET Core PasswordHasher (bcrypt-derived, slow by design).
/// Reset tokens use SHA-256 (fast, appropriate for already-random high-entropy values).
///
/// Never store raw reset tokens. Never log passwords or tokens.
/// </summary>
public interface IPasswordService
{
    /// <summary>Hash a password using a slow adaptive algorithm (bcrypt/PBKDF2).</summary>
    string Hash(string password);

    /// <summary>Verify a plaintext password against a stored hash.</summary>
    bool Verify(string password, string hash);

    /// <summary>
    /// Compute a fast SHA-256 hex-digest of a high-entropy random token.
    /// Used for password reset tokens — NOT for passwords.
    /// </summary>
    string HashToken(string rawToken);

    /// <summary>
    /// Generate a cryptographically secure random reset token (URL-safe base64, 32 bytes of entropy).
    /// The raw value is sent to the user; only the hash is stored.
    /// </summary>
    string GenerateResetToken();
}
