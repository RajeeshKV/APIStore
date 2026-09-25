namespace KromicCommerce.Contracts.Auth;

/// <summary>
/// Returned after successful authentication or token refresh.
/// Raw refresh token is returned once and never stored by the server.
/// The frontend must store it securely (HttpOnly cookie preferred).
/// </summary>
public sealed record TokenResponse(
    string AccessToken,
    string RefreshToken,
    int AccessTokenExpiresInSeconds,
    string TokenType = "Bearer");
