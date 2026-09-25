namespace KromicCommerce.Contracts.Auth;

public sealed record LoginRequest(
    string Email,
    string Password,
    string? DeviceHint = null);
