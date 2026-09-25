namespace KromicCommerce.Contracts.Me;

public sealed record UpdateCustomerProfileRequest(
    string? DisplayName,
    DateOnly? DateOfBirth,
    string? PhoneNumber,
    bool NewsletterConsent,
    string? PreferredTimeZoneId);
