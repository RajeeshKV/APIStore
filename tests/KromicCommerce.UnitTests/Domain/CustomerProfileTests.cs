namespace KromicCommerce.UnitTests.Domain;

public sealed class CustomerProfileTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    // -----------------------------------------------------------------------
    // Create
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_sets_userId_and_leaves_optionals_null()
    {
        var profile = CustomerProfile.Create(UserId);

        profile.UserId.Should().Be(UserId);
        profile.DisplayName.Should().BeNull();
        profile.DateOfBirth.Should().BeNull();
        profile.AvatarUrl.Should().BeNull();
        profile.PhoneNumber.Should().BeNull();
        profile.NewsletterConsent.Should().BeFalse();
        profile.PreferredTimeZoneId.Should().BeNull();
        profile.DomainEvents.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // UpdateProfile
    // -----------------------------------------------------------------------

    [Fact]
    public void UpdateProfile_persists_all_fields()
    {
        var profile = CustomerProfile.Create(UserId);
        var dob = new DateOnly(1990, 6, 15);

        profile.UpdateProfile("Jane D.", dob, "+919876543210", true, "Asia/Kolkata");

        profile.DisplayName.Should().Be("Jane D.");
        profile.DateOfBirth.Should().Be(dob);
        profile.PhoneNumber.Should().Be("+919876543210");
        profile.NewsletterConsent.Should().BeTrue();
        profile.PreferredTimeZoneId.Should().Be("Asia/Kolkata");
    }

    [Fact]
    public void UpdateProfile_trims_string_fields()
    {
        var profile = CustomerProfile.Create(UserId);

        profile.UpdateProfile("  Jane  ", null, "  +91  ", false, "  Asia/Kolkata  ");

        profile.DisplayName.Should().Be("Jane");
        profile.PhoneNumber.Should().Be("+91");
        profile.PreferredTimeZoneId.Should().Be("Asia/Kolkata");
    }

    [Fact]
    public void UpdateProfile_accepts_all_nulls()
    {
        var profile = CustomerProfile.Create(UserId);

        profile.UpdateProfile(null, null, null, false, null);

        profile.DisplayName.Should().BeNull();
        profile.PhoneNumber.Should().BeNull();
        profile.PreferredTimeZoneId.Should().BeNull();
        profile.NewsletterConsent.Should().BeFalse();
    }

    [Fact]
    public void UpdateProfile_raises_CustomerProfileUpdatedEvent()
    {
        var profile = CustomerProfile.Create(UserId);

        profile.UpdateProfile("Jane", null, null, false, null);

        profile.DomainEvents.Should().ContainSingle(e => e is CustomerProfileUpdatedEvent);
        var evt = (CustomerProfileUpdatedEvent)profile.DomainEvents.First();
        evt.UserId.Should().Be(UserId);
    }

    [Fact]
    public void UpdateProfile_newsletter_can_toggle_on_and_off()
    {
        var profile = CustomerProfile.Create(UserId);

        profile.UpdateProfile(null, null, null, true, null);
        profile.NewsletterConsent.Should().BeTrue();

        profile.UpdateProfile(null, null, null, false, null);
        profile.NewsletterConsent.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // SetAvatar
    // -----------------------------------------------------------------------

    [Fact]
    public void SetAvatar_stores_trimmed_url()
    {
        var profile = CustomerProfile.Create(UserId);

        profile.SetAvatar("  https://res.cloudinary.com/demo/image/upload/sample.jpg  ");

        profile.AvatarUrl.Should().Be("https://res.cloudinary.com/demo/image/upload/sample.jpg");
    }

    [Fact]
    public void SetAvatar_raises_CustomerProfileUpdatedEvent()
    {
        var profile = CustomerProfile.Create(UserId);

        profile.SetAvatar("https://example.com/avatar.jpg");

        profile.DomainEvents.Should().ContainSingle(e => e is CustomerProfileUpdatedEvent);
    }

    [Fact]
    public void SetAvatar_overwrites_previous_avatar()
    {
        var profile = CustomerProfile.Create(UserId);
        profile.SetAvatar("https://example.com/v1.jpg");
        profile.SetAvatar("https://example.com/v2.jpg");

        profile.AvatarUrl.Should().Be("https://example.com/v2.jpg");
    }
}
