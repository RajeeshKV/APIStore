namespace KromicCommerce.Application.Abstractions.Security;

/// <summary>
/// Encrypts and decrypts secret strings for safe at-rest storage of
/// customer-configurable integration credentials (Razorpay, Google OAuth, Brevo, SMS).
///
/// The implementation uses ASP.NET Core Data Protection by default.
/// The abstraction allows replacement with a dedicated secret-management service later.
///
/// Rules:
///   - Application code must not depend on encryption implementation details.
///   - Encrypted values may be stored in the database.
///   - Decrypted values must NEVER be returned by GET APIs.
///   - Decrypted values must NEVER be logged.
///   - Protect() / Unprotect() failures must surface as exceptions, not silently return null.
/// </summary>
public interface ISecretProtectionService
{
    /// <summary>Encrypts a plaintext secret for safe database storage.</summary>
    string Protect(string plaintext);

    /// <summary>
    /// Decrypts a previously protected value.
    /// Throws if the value is tampered with or the key has been rotated.
    /// </summary>
    string Unprotect(string ciphertext);

    /// <summary>
    /// Returns a masked display version of a secret (never the actual value).
    /// Example: "rzp_live_abc123" → "rzp_****3"
    /// </summary>
    string Mask(string? plaintext, int visibleSuffix = 4);
}
