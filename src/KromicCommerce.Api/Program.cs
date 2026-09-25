using DotNetEnv;
using KromicCommerce.Api.Extensions;
using KromicCommerce.Api.Middleware;
using KromicCommerce.Infrastructure.Configuration;
using KromicCommerce.Infrastructure.Extensions;
using Serilog;

// ---------------------------------------------------------------------------
// Bootstrap logger — captures startup failures before full Serilog init.
// ---------------------------------------------------------------------------
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    // -----------------------------------------------------------------------
    // Load .env for local development (ignored in production where env vars
    // are injected directly by the container/host).
    // -----------------------------------------------------------------------
    Env.TraversePath().Load();

    var builder = WebApplication.CreateBuilder(args);

    // Add environment variables as a configuration source (highest priority)
    builder.Configuration.AddEnvironmentVariables();

    // -----------------------------------------------------------------------
    // Logging
    // -----------------------------------------------------------------------
    builder.Host.ConfigureSerilog();

    // -----------------------------------------------------------------------
    // Sentry — optional production error monitoring
    // Disabled when Sentry:Dsn is not configured. Never prevents startup.
    // -----------------------------------------------------------------------
    builder.AddSentryMonitoring();

    // -----------------------------------------------------------------------
    // Services
    // -----------------------------------------------------------------------
    builder.Services.AddApplicationServices();
    builder.Services.AddInfrastructureServices(builder.Configuration);

    builder.Services.AddApiVersioningServices();
    builder.Services.AddSwaggerServices();
    builder.Services.AddHealthCheckServices(builder.Configuration);
    builder.Services.AddCorsPolicy(builder.Configuration);

    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddRateLimitingPolicies(builder.Configuration);

    // -----------------------------------------------------------------------
    // Build
    // -----------------------------------------------------------------------
    var app = builder.Build();

    // -----------------------------------------------------------------------
    // Database migration — must complete before serving any API traffic.
    // Controlled by App__Migrate=true (disabled by default).
    // Fatal if migration fails — prevents running against incompatible schema.
    // -----------------------------------------------------------------------
    await app.ApplyDatabaseMigrationsAsync();

    // -----------------------------------------------------------------------
    // Middleware pipeline
    // -----------------------------------------------------------------------
    app.UseMiddleware<GlobalExceptionMiddleware>();

    if (app.Environment.IsDevelopment())
        app.UseSwaggerWithVersioning();

    app.UseHttpsRedirection();
    app.UseCors(CorsOptions.PolicyName);

    app.UseSerilogRequestLogging(opts =>
    {
        // Keep request log noise low; errors still surface through the exception middleware
        opts.MessageTemplate =
            "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
    });

    app.UseAuthentication();
    app.UseAuthorization();
    app.UseRateLimiter();

    app.MapControllers();

    app.MapHealthChecks("/health");
    app.MapHealthChecks("/health/ready", new()
    {
        Predicate = check => check.Tags.Contains("db")
    });

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application terminated unexpectedly during startup");
}
finally
{
    Log.CloseAndFlush();
}

// Exposed for integration test WebApplicationFactory
public partial class Program { }
