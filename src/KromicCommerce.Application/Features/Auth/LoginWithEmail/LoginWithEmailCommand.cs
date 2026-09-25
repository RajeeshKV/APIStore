namespace KromicCommerce.Application.Features.Auth.LoginWithEmail;

public sealed record LoginWithEmailCommand(
    string Email,
    string Password,
    string? DeviceHint) : ICommand<TokenResponse>;
