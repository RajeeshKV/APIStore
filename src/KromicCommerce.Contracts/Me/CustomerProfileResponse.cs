namespace KromicCommerce.Contracts.Me;

/// <param name="PhoneNumber">The verified account phone, or null.</param>
/// <param name="PhoneNumberVerified">Whether that number has been proven.</param>
/// <param name="PendingPhoneNumber">
/// A number submitted for change but not yet verified, or null. Surfaced so the UI can say
/// "pending verification" instead of silently showing the old number and letting the customer
/// believe the change took effect.
/// </param>
public sealed record CustomerProfileResponse(
    Guid UserId,
    string Email,
    string? FirstName,
    string? LastName,
    string? DisplayName,
    string? PhoneNumber,
    bool PhoneNumberVerified,
    string? PendingPhoneNumber,
    string? AvatarUrl,
    DateOnly? DateOfBirth,
    bool NewsletterConsent,
    string? PreferredTimeZoneId,
    DateTime? LastLoginAt,
    DateTime UpdatedAtUtc);
