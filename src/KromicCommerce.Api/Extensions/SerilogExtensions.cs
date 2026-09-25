using Serilog;
using Serilog.Events;

namespace KromicCommerce.Api.Extensions;

internal static class SerilogExtensions
{
    /// <summary>
    /// Configures Serilog as the host logger from appsettings.
    /// Called on the HostBuilder before the DI container is built so that
    /// startup errors (including configuration validation failures) are captured.
    /// All configuration comes from the "Serilog" section in appsettings / environment variables.
    /// </summary>
    internal static void ConfigureSerilog(this IHostBuilder host)
    {
        host.UseSerilog((context, services, config) =>
        {
            config
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithEnvironmentName()
                .Enrich.WithProcessId()
                .Enrich.WithThreadId();
        });
    }
}
