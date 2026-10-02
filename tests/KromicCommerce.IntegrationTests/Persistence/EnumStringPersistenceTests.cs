using KromicCommerce.IntegrationTests.Infrastructure;

namespace KromicCommerce.IntegrationTests.Persistence;

[Collection("Database")]
public sealed class EnumStringPersistenceTests(DatabaseFixture db)
    : IntegrationTestBase(db)
{
    [SkippableFact]
    public async Task UserRole_is_persisted_as_string()
    {
        await using var ctx = Db.CreateDbContext();
        var user = User.CreateCustomer($"enumtest_{Guid.NewGuid():N}@example.com", "hash", "A", "B");
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        // Quoted, because EF maps these to PascalCase columns and an unquoted identifier folds to
        // lowercase in PostgreSQL, which does not match.
        var rawRole = await ctx.Database.SqlQueryRaw<string>(
            "SELECT \"Role\" AS \"Value\" FROM users WHERE \"Id\" = {0}", user.Id)
            .FirstAsync();

        rawRole.Should().Be("Customer");
    }

    [SkippableFact]
    public async Task OtpPurpose_is_persisted_as_string()
    {
        await using var ctx = Db.CreateDbContext();
        var otp = OtpRequest.Create($"+9199{Guid.NewGuid().ToString("N")[..8]}",
            "hash", OtpPurpose.PhoneVerification, DateTime.UtcNow.AddMinutes(10));
        ctx.OtpRequests.Add(otp);
        await ctx.SaveChangesAsync();

        var rawPurpose = await ctx.Database.SqlQueryRaw<string>(
            "SELECT \"Purpose\" AS \"Value\" FROM otp_requests WHERE \"Id\" = {0}", otp.Id)
            .FirstAsync();

        rawPurpose.Should().Be("PhoneVerification");
    }
}
