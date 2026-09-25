namespace KromicCommerce.Contracts.Auth;

/// <summary>
/// One-time admin bootstrap request. Rejected if an admin already exists.
/// Must be protected by the bootstrap secret in production.
/// </summary>
public sealed record BootstrapAdminRequest(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    string BusinessName,
    string BootstrapSecret,
    string? Username = null);
