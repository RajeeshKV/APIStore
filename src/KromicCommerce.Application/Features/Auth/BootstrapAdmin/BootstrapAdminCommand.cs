namespace KromicCommerce.Application.Features.Auth.BootstrapAdmin;

public sealed record BootstrapAdminCommand(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    string BusinessName,
    string BootstrapSecret,
    string? Username = null) : ICommand<TokenResponse>;
