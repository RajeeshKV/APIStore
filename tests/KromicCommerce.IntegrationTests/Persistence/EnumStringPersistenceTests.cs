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

        var rawRole = await ctx.Database.SqlQueryRaw<string>(
            "SELECT role FROM users WHERE id = {0}", user.Id)
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
            "SELECT purpose FROM otp_requests WHERE id = {0}", otp.Id)
            .FirstAsync();

        rawPurpose.Should().Be("PhoneVerification");
    }
}
