namespace KromicCommerce.Application.Features.Auth.GoogleCallback;

public sealed record GoogleCallbackCommand(
    string IdToken,
    string? DeviceHint) : ICommand<TokenResponse>;
