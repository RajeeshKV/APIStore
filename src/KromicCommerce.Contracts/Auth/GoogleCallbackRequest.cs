namespace KromicCommerce.Contracts.Auth;

/// <summary>
/// Frontend sends the Google ID token (credential) it received from Google Sign-In.
/// The backend validates the token against Google's public keys — never trusts it blindly.
/// </summary>
public sealed record GoogleCallbackRequest(
    string IdToken,
    string? DeviceHint = null);
