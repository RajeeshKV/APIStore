namespace KromicCommerce.Application.Abstractions.Auth;

/// <summary>
/// Validates a Google ID token and extracts verified identity claims.
/// The application never trusts client-supplied claims directly.
/// </summary>
public interface IGoogleAuthService
{
    /// <summary>
    /// Validates the Google ID token and returns verified identity information.
    /// Returns null if the token is invalid, expired, or untrusted.
    /// </summary>
    Task<GoogleIdentity?> ValidateIdTokenAsync(string idToken, CancellationToken cancellationToken = default);
}

/// <summary>Verified Google identity extracted from a validated ID token.</summary>
public sealed record GoogleIdentity(
    string Subject,      // stable "sub" claim — use this as the permanent identity key
    string Email,
    bool EmailVerified,
    string? GivenName,
    string? FamilyName,
    string? PictureUrl);
