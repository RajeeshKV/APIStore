namespace KromicCommerce.Application.Features.Auth.LogoutAll;

public sealed record LogoutAllCommand(Guid UserId) : ICommand;
