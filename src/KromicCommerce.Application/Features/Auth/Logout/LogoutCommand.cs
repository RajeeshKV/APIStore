namespace KromicCommerce.Application.Features.Auth.Logout;

public sealed record LogoutCommand(
    string RawRefreshToken,
    Guid UserId) : ICommand;
