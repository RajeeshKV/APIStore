namespace KromicCommerce.UnitTests.Domain;

public sealed class UserTests
{
    [Fact]
    public void CreateCustomer_sets_role_and_normalizes_email()
    {
        var user = User.CreateCustomer("Test@Example.COM", "hash", "Jane", "Doe");

        user.Email.Should().Be("test@example.com");
        user.NormalizedEmail.Should().Be("TEST@EXAMPLE.COM");
        user.Role.Should().Be(UserRole.Customer);
        user.IsActive.Should().BeTrue();
        user.TokenVersion.Should().Be(1);
    }

    [Fact]
    public void CreateAdmin_sets_admin_role()
    {
        var user = User.CreateAdmin("admin@example.com", "hash", "Admin", "User");
        user.Role.Should().Be(UserRole.Admin);
    }

    [Fact]
    public void IncrementTokenVersion_increments_by_one()
    {
        var user = User.CreateCustomer("a@b.com", null, "A", "B");
        var before = user.TokenVersion;
        user.IncrementTokenVersion();
        user.TokenVersion.Should().Be(before + 1);
    }

    [Fact]
    public void RecordLogin_raises_UserLoggedInEvent()
    {
        var user = User.CreateCustomer("a@b.com", null, "A", "B");
        user.ClearDomainEvents();
        user.RecordLogin();
        user.DomainEvents.Should().ContainSingle(e => e is UserLoggedInEvent);
    }

    [Fact]
    public void CreateCustomer_raises_UserRegisteredEvent()
    {
        var user = User.CreateCustomer("a@b.com", null, "A", "B");
        user.DomainEvents.Should().ContainSingle(e => e is UserRegisteredEvent);
    }

    [Fact]
    public void SetPhoneNumber_stores_trimmed_number()
    {
        var user = User.CreateCustomer("a@b.com", null, "A", "B");
        user.SetPhoneNumber(" +919876543210 ", verified: true);
        user.PhoneNumber.Should().Be("+919876543210");
        user.PhoneNumberVerified.Should().BeTrue();
    }

    [Fact]
    public void Requesting_a_change_leaves_the_verified_number_alone()
    {
        // The whole point of staging: a number the customer has not yet proven they control must
        // never overwrite one they have. Otherwise anyone could take an account over by editing
        // the phone field, and delivery would go to a number nobody verified.
        var user = User.CreateCustomer("a@b.com", null, "A", "B");
        user.SetPhoneNumber("+919876543210", verified: true);

        user.RequestPhoneNumberChange("+919999999999");

        user.PhoneNumber.Should().Be("+919876543210");
        user.PhoneNumberVerified.Should().BeTrue();
        user.PendingPhoneNumber.Should().Be("+919999999999");
    }

    [Fact]
    public void Promoting_a_pending_number_makes_it_the_verified_one()
    {
        var user = User.CreateCustomer("a@b.com", null, "A", "B");
        user.SetPhoneNumber("+919876543210", verified: true);
        user.RequestPhoneNumberChange("+919999999999");

        user.PromotePendingPhoneNumber("+919999999999");

        user.PhoneNumber.Should().Be("+919999999999");
        user.PhoneNumberVerified.Should().BeTrue();
        // Exactly one number, never a lingering pending value to confuse a later read.
        user.PendingPhoneNumber.Should().BeNull();
    }

    [Fact]
    public void Requesting_the_number_already_verified_asks_for_nothing()
    {
        var user = User.CreateCustomer("a@b.com", null, "A", "B");
        user.SetPhoneNumber("+919876543210", verified: true);

        user.RequestPhoneNumberChange("+919876543210");

        // No verification round trip for a number already proven: the customer would be sent an
        // OTP for the number they already own, which is pure friction.
        user.PendingPhoneNumber.Should().BeNull();
        user.PhoneNumberVerified.Should().BeTrue();
    }

    [Fact]
    public void A_pending_change_survives_until_it_is_verified_or_abandoned()
    {
        var user = User.CreateCustomer("a@b.com", null, "A", "B");
        user.SetPhoneNumber("+919876543210", verified: true);
        user.RequestPhoneNumberChange("+919999999999");

        user.ClearPendingPhoneNumber();

        user.PendingPhoneNumber.Should().BeNull();
        // Abandoning a change must never cost the customer their verified number.
        user.PhoneNumber.Should().Be("+919876543210");
        user.PhoneNumberVerified.Should().BeTrue();
    }

    [Fact]
    public void Verifying_the_original_number_clears_a_pending_replacement()
    {
        var user = User.CreateCustomer("a@b.com", null, "A", "B");
        user.SetPhoneNumber("+919876543210", verified: true);
        user.RequestPhoneNumberChange("+919999999999");

        user.SetPhoneNumber("+919876543210", verified: true);

        // An account with a proven number has nothing pending; leaving a stale value here would
        // show the customer a number they abandoned as though it were still queued.
        user.PendingPhoneNumber.Should().BeNull();
    }

    [Fact]
    public void FullName_combines_first_and_last()
    {
        var user = User.CreateCustomer("a@b.com", null, "Jane", "Doe");
        user.FullName.Should().Be("Jane Doe");
    }
}
