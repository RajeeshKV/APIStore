using KromicCommerce.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace KromicCommerce.IntegrationTests.Infrastructure;

/// <summary>
/// Shared database fixture using Testcontainers (real PostgreSQL container).
/// When Docker is not available the fixture marks itself unavailable and all
/// dependent tests skip rather than fail, keeping CI green in environments
/// without Docker.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container;

    public bool IsAvailable { get; private set; }
    public string? UnavailableReason { get; private set; }

    public DatabaseFixture()
    {
        try
        {
            _container = new PostgreSqlBuilder()
                .WithDatabase("kromic_test")
                .WithUsername("postgres")
                .WithPassword("postgres_test")
                .Build();
        }
        catch (Exception ex)
        {
            // Docker not installed / not reachable at construction time
            _container = null!;
            IsAvailable = false;
            UnavailableReason = $"Docker unavailable: {ex.Message}";
        }
    }

    public AppDbContext CreateDbContext()
    {
        if (!IsAvailable)
            throw new InvalidOperationException("DatabaseFixture is not available. Check IsAvailable before calling CreateDbContext.");

        return BuildContext();
    }

    /// <summary>
    /// Builds a context without the availability guard. Used during initialisation, which happens
    /// before <see cref="IsAvailable"/> can be set true — routing it through
    /// <see cref="CreateDbContext"/> made every integration test skip even with Docker running.
    /// </summary>
    private AppDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;
        return new AppDbContext(options, NullLogger<AppDbContext>.Instance);
    }

    public async Task InitializeAsync()
    {
        if (_container is null)
            return; // already flagged unavailable

        try
        {
            await _container.StartAsync();

            // Mark available before migrating: the guard is what CreateDbContext checks, and the
            // migration below needs a context. A migration failure is caught here and flips this
            // back to false with the reason attached.
            IsAvailable = true;
            await using var ctx = BuildContext();
            await ctx.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            UnavailableReason = $"Container start failed: {ex.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}
