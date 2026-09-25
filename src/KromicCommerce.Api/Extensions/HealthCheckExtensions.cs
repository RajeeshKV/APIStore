using KromicCommerce.Infrastructure.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KromicCommerce.Api.Extensions;

internal static class HealthCheckExtensions
{
    internal static IServiceCollection AddHealthCheckServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration
            .GetSection(DatabaseOptions.SectionName)
            .Get<DatabaseOptions>()
            ?.ConnectionString;

        var builder = services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy("API is running."));

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            builder.AddNpgSql(
                connectionString,
                name: "postgresql",
                tags: ["db", "postgresql"]);
        }

        return services;
    }
}
