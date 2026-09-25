namespace KromicCommerce.Application.Features.Auth.RefreshToken;

public sealed record RefreshTokenCommand(
    string RawRefreshToken,
    string? DeviceHint) : ICommand<TokenResponse>;
