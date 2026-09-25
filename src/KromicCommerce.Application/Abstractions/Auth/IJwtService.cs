namespace KromicCommerce.Application.Abstractions.Auth;

/// <summary>
/// Issues and validates application JWT access tokens.
/// Token versioning: the TokenVersion claim is embedded and validated on every request.
/// </summary>
public interface IJwtService
{
    /// <summary>Issues a signed access token for the given user.</summary>
    string IssueAccessToken(Guid userId, string email, string role, int tokenVersion);

    /// <summary>
    /// Validates a token and returns the claims principal.
    /// Returns null if the token is invalid, expired, or has an outdated TokenVersion.
    /// </summary>
    System.Security.Claims.ClaimsPrincipal? ValidateToken(string token);
}
