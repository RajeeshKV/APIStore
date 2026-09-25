namespace KromicCommerce.Application.Features.Me.Profile;

public sealed record UpdateCustomerProfileCommand(
    Guid UserId,
    string? DisplayName,
    DateOnly? DateOfBirth,
    string? PhoneNumber,
    bool NewsletterConsent,
    string? PreferredTimeZoneId) : ICommand<CustomerProfileResponse>;
