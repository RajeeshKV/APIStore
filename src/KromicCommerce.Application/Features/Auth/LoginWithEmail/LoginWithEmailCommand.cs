namespace KromicCommerce.Application.Features.Auth.LoginWithEmail;

/// <summary>
/// Authenticate using either an email address or a username plus a password.
/// The <see cref="Identifier"/> field accepts either form — the handler resolves both.
/// </summary>
public sealed record LoginWithEmailCommand(
    string Identifier,
    string Password,
    string? DeviceHint) : ICommand<TokenResponse>;
