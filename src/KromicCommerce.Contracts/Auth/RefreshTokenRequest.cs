namespace KromicCommerce.Contracts.Auth;

public sealed record RefreshTokenRequest(
    string RefreshToken,
    string? DeviceHint = null);
