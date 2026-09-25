using KromicCommerce.Domain.Identity.Events;

namespace KromicCommerce.Domain.Identity;

/// <summary>
/// Extended customer profile data, separate from the core User identity.
/// Keeps identity (auth) concerns in User; commerce/preference concerns here.
/// Phone number here is optional display/preference — the auth phone is on User.
/// </summary>
public sealed class CustomerProfile : AuditableEntity
{
    private CustomerProfile() { } // EF constructor

    public static CustomerProfile Create(Guid userId)
        => new()
        {
            UserId = userId
        };

    public Guid UserId { get; private set; }

    /// <summary>Preferred display name (may differ from User.FirstName/LastName).</summary>
    public string? DisplayName { get; private set; }

    /// <summary>Date of birth for personalisation — never used for age-gate enforcement here.</summary>
    public DateOnly? DateOfBirth { get; private set; }

    /// <summary>Profile picture URL (Cloudinary URL).</summary>
    public string? AvatarUrl { get; private set; }

    /// <summary>Optional preferred phone number for order/delivery contact.</summary>
    public string? PhoneNumber { get; private set; }

    /// <summary>Customer has opted in to marketing/newsletter communications.</summary>
    public bool NewsletterConsent { get; private set; }

    /// <summary>Preferred timezone override (IANA, e.g. Asia/Kolkata). Null = use store timezone.</summary>
    public string? PreferredTimeZoneId { get; private set; }

    // Navigation
    public User User { get; private set; } = null!;

    // -----------------------------------------------------------------------
    // Behaviour
    // -----------------------------------------------------------------------

    public void UpdateProfile(
        string? displayName,
        DateOnly? dateOfBirth,
        string? phoneNumber,
        bool newsletterConsent,
        string? preferredTimeZoneId)
    {
        DisplayName = displayName?.Trim();
        DateOfBirth = dateOfBirth;
        PhoneNumber = phoneNumber?.Trim();
        NewsletterConsent = newsletterConsent;
        PreferredTimeZoneId = preferredTimeZoneId?.Trim();

        RaiseDomainEvent(new CustomerProfileUpdatedEvent(UserId));
    }

    public void SetAvatar(string url)
    {
        AvatarUrl = url.Trim();
        RaiseDomainEvent(new CustomerProfileUpdatedEvent(UserId));
    }
}
