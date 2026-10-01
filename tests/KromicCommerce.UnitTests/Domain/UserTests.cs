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
    public void Proving_the_pending_number_promotes_it()
    {
        var user = User.CreateCustomer("a@b.com", null, "A", "B");
        user.SetPhoneNumber("+919876543210", verified: true);
        user.RequestPhoneNumberChange("+919999999999");

        user.TryCompletePhoneVerification("+919999999999").Should().BeTrue();

        user.PhoneNumber.Should().Be("+919999999999");
        user.PhoneNumberVerified.Should().BeTrue();
        // Exactly one number, never a lingering pending value to confuse a later read.
        user.PendingPhoneNumber.Should().BeNull();
    }

    [Fact]
    public void Proving_a_number_that_is_no_longer_pending_changes_nothing()
    {
        // The regression this guards: the OTP for an abandoned change is still cryptographically
        // valid, so without this check it would reinstate a number the customer moved on from and
        // silently discard the one they are actually trying to verify.
        var user = User.CreateCustomer("a@b.com", null, "A", "B");
        user.SetPhoneNumber("+919876543210", verified: true);
        user.RequestPhoneNumberChange("+918888888888");
        user.RequestPhoneNumberChange("+917777777777");

        user.TryCompletePhoneVerification("+918888888888").Should().BeFalse();

        // The verified number survives, and the pending one is still the live request.
        user.PhoneNumber.Should().Be("+919876543210");
        user.PhoneNumberVerified.Should().BeTrue();
        user.PendingPhoneNumber.Should().Be("+917777777777");
    }

    [Fact]
    public void Proving_the_registration_number_verifies_it()
    {
        // Registration sets the phone directly with nothing pending, so that number is the one
        // awaiting proof. Without this the very first verification could never complete.
        var user = User.CreateCustomer("a@b.com", null, "A", "B");
        user.SetPhoneNumber("+919876543210", verified: false);

        user.TryCompletePhoneVerification("+919876543210").Should().BeTrue();

        user.PhoneNumberVerified.Should().BeTrue();
    }

    [Fact]
    public void Proving_an_unrelated_number_cannot_overwrite_a_verified_one()
    {
        // No change was requested, so the only number awaiting proof is the verified one. A code
        // for any other number must not be able to become the account phone.
        var user = User.CreateCustomer("a@b.com", null, "A", "B");
        user.SetPhoneNumber("+919876543210", verified: true);

        user.TryCompletePhoneVerification("+918888888888").Should().BeFalse();

        user.PhoneNumber.Should().Be("+919876543210");
        user.PhoneNumberVerified.Should().BeTrue();
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
