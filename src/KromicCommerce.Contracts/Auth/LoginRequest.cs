namespace KromicCommerce.Contracts.Auth;

/// <summary>
/// Admin login request. <see cref="Identifier"/> accepts either an email address
/// or a username — the backend resolves both.
/// </summary>
public sealed record LoginRequest(
    string Identifier,
    string Password,
    string? DeviceHint = null);
