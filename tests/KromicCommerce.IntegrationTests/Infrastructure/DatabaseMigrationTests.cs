using KromicCommerce.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace KromicCommerce.IntegrationTests.Infrastructure;

/// <summary>
/// Verifies EF Core migration behavior against a real PostgreSQL database.
/// Requires Docker — tests skip gracefully when Docker is unavailable.
/// </summary>
[Collection("Database")]
public sealed class DatabaseMigrationTests(DatabaseFixture db)
    : IntegrationTestBase(db)
{
    [SkippableFact]
    public async Task MigrateAsync_applies_all_migrations_to_clean_database()
    {
        // DatabaseFixture already calls Database.MigrateAsync() in InitializeAsync.
        // Verify the result: no pending migrations should remain.
        await using var ctx = Db.CreateDbContext();

        var pending = (await ctx.Database.GetPendingMigrationsAsync()).ToList();

        pending.Should().BeEmpty(
            "all migrations should have been applied by the fixture");
    }

    [SkippableFact]
    public async Task MigrateAsync_is_idempotent_when_already_up_to_date()
    {
        // Calling MigrateAsync again on an already-migrated database must not throw.
        await using var ctx = Db.CreateDbContext();
        var act = async () => await ctx.Database.MigrateAsync();
        await act.Should().NotThrowAsync(
            "MigrateAsync must be idempotent when no migrations are pending");
    }

    [SkippableFact]
    public async Task Applied_migrations_list_is_non_empty()
    {
        // Sanity check: at least the initial create migration should be applied.
        await using var ctx = Db.CreateDbContext();
        var applied = (await ctx.Database.GetAppliedMigrationsAsync()).ToList();

        applied.Should().NotBeEmpty(
            "at least InitialCreate must have been applied");
        applied.Should().Contain(m => m.Contains("InitialCreate"),
            "the baseline migration must be in the applied list");
    }

    [SkippableFact]
    public async Task Database_can_connect_after_migration()
    {
        await using var ctx = Db.CreateDbContext();
        var canConnect = await ctx.Database.CanConnectAsync();
        canConnect.Should().BeTrue();
    }
}
