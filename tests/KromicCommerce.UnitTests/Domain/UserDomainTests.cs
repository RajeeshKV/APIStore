namespace KromicCommerce.UnitTests.Domain;

/// <summary>
/// Tests for Phase 8 User domain additions: Username, PasswordResetToken, ResetPassword.
/// </summary>
public sealed class UserDomainTests
{
    private static User BuildAdmin() =>
        User.CreateAdmin("admin@example.com", "hash", "Alice", "Admin");

    // -----------------------------------------------------------------------
    // Username
    // -----------------------------------------------------------------------

    [Fact]
    public void SetUsername_persists_trimmed_username_and_normalised_form()
    {
        var user = BuildAdmin();
        user.SetUsername("  Alice123  ");
        user.Username.Should().Be("Alice123");
        user.NormalizedUsername.Should().Be("ALICE123");
    }

    [Fact]
    public void SetUsername_overwrites_previous_value()
    {
        var user = BuildAdmin();
        user.SetUsername("first");
        user.SetUsername("second");
        user.Username.Should().Be("second");
        user.NormalizedUsername.Should().Be("SECOND");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SetUsername_throws_for_empty(string name)
    {
        var user = BuildAdmin();
        var act = () => user.SetUsername(name);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SetUsername_throws_for_too_short()
    {
        var user = BuildAdmin();
        var act = () => user.SetUsername("ab");
        act.Should().Throw<ArgumentException>().WithMessage("*3 characters*");
    }

    [Fact]
    public void SetUsername_throws_for_too_long()
    {
        var user = BuildAdmin();
        var act = () => user.SetUsername(new string('a', 51));
        act.Should().Throw<ArgumentException>().WithMessage("*50 characters*");
    }

    // -----------------------------------------------------------------------
    // SetPasswordResetToken / ClearPasswordResetToken
    // -----------------------------------------------------------------------

    [Fact]
    public void SetPasswordResetToken_stores_hash_and_expiry()
    {
        var user = BuildAdmin();
        var expiry = DateTime.UtcNow.AddMinutes(15);

        user.SetPasswordResetToken("abc123hash", expiry);

        user.PasswordResetTokenHash.Should().Be("abc123hash");
        user.PasswordResetTokenExpiresAt.Should().BeCloseTo(expiry, precision: TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ClearPasswordResetToken_nullifies_both_fields()
    {
        var user = BuildAdmin();
        user.SetPasswordResetToken("hash", DateTime.UtcNow.AddMinutes(15));
        user.ClearPasswordResetToken();

        user.PasswordResetTokenHash.Should().BeNull();
        user.PasswordResetTokenExpiresAt.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // ResetPassword
    // -----------------------------------------------------------------------

    [Fact]
    public void ResetPassword_updates_hash_and_increments_token_version()
    {
        var user = BuildAdmin();
        var initialVersion = user.TokenVersion;
        user.SetPasswordResetToken("hash", DateTime.UtcNow.AddMinutes(15));

        user.ResetPassword("newPasswordHash");

        user.PasswordHash.Should().Be("newPasswordHash");
        user.TokenVersion.Should().Be(initialVersion + 1);
    }

    [Fact]
    public void ResetPassword_clears_reset_token()
    {
        var user = BuildAdmin();
        user.SetPasswordResetToken("hash", DateTime.UtcNow.AddMinutes(15));

        user.ResetPassword("newHash");

        user.PasswordResetTokenHash.Should().BeNull();
        user.PasswordResetTokenExpiresAt.Should().BeNull();
    }

    [Fact]
    public void ResetPassword_incremented_token_version_invalidates_old_jwts()
    {
        var user = BuildAdmin();
        var versionBefore = user.TokenVersion;

        user.ResetPassword("anotherHash");

        // TokenVersion is embedded in JWT — any JWT with the old version will be rejected
        user.TokenVersion.Should().BeGreaterThan(versionBefore);
    }

    // -----------------------------------------------------------------------
    // IncrementTokenVersion (used by logout-all and password reset)
    // -----------------------------------------------------------------------

    [Fact]
    public void IncrementTokenVersion_increases_by_one()
    {
        var user = BuildAdmin();
        var before = user.TokenVersion;
        user.IncrementTokenVersion();
        user.TokenVersion.Should().Be(before + 1);
    }
}
