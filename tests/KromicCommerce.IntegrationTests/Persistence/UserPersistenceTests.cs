using KromicCommerce.IntegrationTests.Infrastructure;

namespace KromicCommerce.IntegrationTests.Persistence;

[Collection("Database")]
public sealed class UserPersistenceTests(DatabaseFixture db)
    : IntegrationTestBase(db)
{
    [SkippableFact]
    public async Task Can_persist_and_reload_user_with_profile()
    {
        await using var ctx = Db.CreateDbContext();
        var email = $"persist_{Guid.NewGuid():N}@example.com";
        var user = User.CreateCustomer(email, "hash", "Alice", "Smith");
        var profile = CustomerProfile.Create(user.Id);

        ctx.Users.Add(user);
        ctx.CustomerProfiles.Add(profile);
        await ctx.SaveChangesAsync();

        await using var ctx2 = Db.CreateDbContext();
        var loaded = await ctx2.Users
            .Include(u => u.CustomerProfile)
            .FirstAsync(u => u.Id == user.Id);

        loaded.Email.Should().Be(email.ToLowerInvariant());
        loaded.Role.Should().Be(UserRole.Customer);
        loaded.TokenVersion.Should().Be(1);
        loaded.CustomerProfile.Should().NotBeNull();
    }

    [SkippableFact]
    public async Task CreatedAtUtc_is_stored_as_utc()
    {
        await using var ctx = Db.CreateDbContext();
        var user = User.CreateCustomer($"utc_{Guid.NewGuid():N}@example.com", null, "B", "C");
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        await using var ctx2 = Db.CreateDbContext();
        var loaded = await ctx2.Users.FirstAsync(u => u.Id == user.Id);
        loaded.CreatedAtUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [SkippableFact]
    public async Task ExternalLogin_unique_constraint_prevents_duplicates()
    {
        await using var ctx = Db.CreateDbContext();
        var email = $"ext_{Guid.NewGuid():N}@example.com";
        var user = User.CreateCustomer(email, null, "C", "D");
        var subject = $"sub_{Guid.NewGuid():N}"; // unique subject per test run
        var login1 = ExternalLogin.Create(user.Id, "Google", subject, email);
        var login2 = ExternalLogin.Create(user.Id, "Google", subject, email); // duplicate

        ctx.Users.Add(user);
        ctx.ExternalLogins.Add(login1);
        await ctx.SaveChangesAsync();

        await using var ctx2 = Db.CreateDbContext();
        ctx2.ExternalLogins.Add(login2);
        var act = async () => await ctx2.SaveChangesAsync();
        await act.Should().ThrowAsync<Exception>();
    }

    [SkippableFact]
    public async Task RefreshToken_can_be_revoked_and_reloaded()
    {
        await using var ctx = Db.CreateDbContext();
        var user = User.CreateCustomer($"rt_{Guid.NewGuid():N}@example.com", null, "D", "E");
        var token = Domain.Identity.RefreshToken.Create(
            user.Id, $"hash_{Guid.NewGuid():N}", DateTime.UtcNow.AddDays(30));
        ctx.Users.Add(user);
        ctx.RefreshTokens.Add(token);
        await ctx.SaveChangesAsync();

        token.Revoke();
        await ctx.SaveChangesAsync();

        await using var ctx2 = Db.CreateDbContext();
        var loaded = await ctx2.RefreshTokens.FirstAsync(t => t.Id == token.Id);
        loaded.IsRevoked.Should().BeTrue();
        loaded.RevokedAt.Should().NotBeNull();
    }
}
