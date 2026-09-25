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
    public void FullName_combines_first_and_last()
    {
        var user = User.CreateCustomer("a@b.com", null, "Jane", "Doe");
        user.FullName.Should().Be("Jane Doe");
    }
}
