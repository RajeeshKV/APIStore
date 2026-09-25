namespace KromicCommerce.Contracts.Me;

public sealed record CustomerProfileResponse(
    Guid UserId,
    string Email,
    string? FirstName,
    string? LastName,
    string? DisplayName,
    string? PhoneNumber,
    bool PhoneNumberVerified,
    string? AvatarUrl,
    DateOnly? DateOfBirth,
    bool NewsletterConsent,
    string? PreferredTimeZoneId,
    DateTime? LastLoginAt,
    DateTime UpdatedAtUtc);
