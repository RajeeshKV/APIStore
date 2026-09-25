namespace KromicCommerce.Contracts.Auth;

public sealed record MeResponse(
    Guid Id,
    string Email,
    string? FirstName,
    string? LastName,
    string? PhoneNumber,
    bool PhoneNumberVerified,
    string Role,
    bool IsActive,
    DateTime? EmailVerifiedAt,
    DateTime? LastLoginAt);
