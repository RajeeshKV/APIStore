namespace KromicCommerce.Application.Abstractions.Auth;

/// <summary>
/// Validates a Google ID token and extracts verified identity claims.
/// The application never trusts client-supplied claims directly.
///
/// The clientId parameter is the Google OAuth Client ID loaded from the database
/// (BusinessSettings.Auth.GoogleClientId). It is passed explicitly so this service
/// does not need to read from environment-variable-backed IOptions — credentials now
/// live in the database and are injected at the call site.
/// </summary>
public interface IGoogleAuthService
{
    /// <summary>
    /// Validates the Google ID token against the supplied client ID and returns
    /// verified identity information. Returns null if the token is invalid,
    /// expired, has the wrong audience, or is otherwise untrusted.
    /// </summary>
    Task<GoogleIdentity?> ValidateIdTokenAsync(
        string idToken,
        string clientId,
        CancellationToken cancellationToken = default);
}

/// <summary>Verified Google identity extracted from a validated ID token.</summary>
public sealed record GoogleIdentity(
    string Subject,       // stable "sub" claim — use this as the permanent identity key
    string Email,
    bool EmailVerified,
    string? GivenName,
    string? FamilyName,
    string? PictureUrl);
