using KromicCommerce.Infrastructure.Configuration;
using KromicCommerce.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KromicCommerce.Infrastructure.Extensions;

/// <summary>
/// Application-owned EF Core migration extension.
///
/// Usage in Program.cs (called once after app.Build(), before middleware):
///   await app.ApplyDatabaseMigrationsAsync();
///
/// Controlled by environment variable: App__Migrate=true
///
/// Rules:
///   - Uses Database.MigrateAsync() — incremental, data-preserving.
///   - Never uses EnsureCreated() or EnsureDeleted() in this path.
///   - Migration failure is fatal: the application must not serve traffic
///     against an incompatible schema.
///   - No secrets (connection strings, passwords) are written to logs.
///   - All migration activity is logged at appropriate severity levels.
///   - Idempotent: if the database is already up to date, startup continues normally.
/// </summary>
public static class DatabaseMigrationExtensions
{
    /// <summary>
    /// Checks whether migration mode is enabled (App__Migrate=true).
    /// If enabled, applies all pending EF Core migrations before the API starts.
    /// Throws on failure — prevents startup against an incompatible schema.
    /// </summary>
    public static async Task ApplyDatabaseMigrationsAsync(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("KromicCommerce.Migration");

        var appOptions = app.Services.GetRequiredService<IOptions<AppOptions>>().Value;

        if (!appOptions.Migrate)
        {
            logger.LogInformation(
                "Database migration mode is disabled (App:Migrate = false). " +
                "Set App__Migrate=true to enable automatic migration on startup.");
            return;
        }

        logger.LogInformation("Database migration mode enabled (App:Migrate = true).");
        logger.LogInformation("Checking for pending EF Core migrations...");

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        try
        {
            // Verify database connectivity before attempting migration
            var canConnect = await db.Database.CanConnectAsync();
            if (!canConnect)
                throw new InvalidOperationException(
                    "Cannot connect to the database. " +
                    "Verify Database:ConnectionString is correctly configured.");

            var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();

            if (pending.Count == 0)
            {
                logger.LogInformation(
                    "Database is up to date. No pending migrations found.");
                return;
            }

            logger.LogInformation(
                "Applying {Count} pending database migration(s): {Migrations}",
                pending.Count,
                string.Join(", ", pending));

            await db.Database.MigrateAsync();

            logger.LogInformation(
                "Database migrations completed successfully. " +
                "{Count} migration(s) applied.",
                pending.Count);
        }
        catch (Exception ex)
        {
            // Log the error but NOT the connection string, password, or any secret
            logger.LogCritical(ex,
                "Database migration failed. Application startup aborted. " +
                "Check the database connectivity and migration state. " +
                "Do not include credentials in error reports.");
            throw; // Fatal — prevents the application from serving requests
        }
    }
}
