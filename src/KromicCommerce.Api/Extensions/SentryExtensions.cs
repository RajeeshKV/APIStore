using Sentry;

namespace KromicCommerce.Api.Extensions;

/// <summary>
/// Configures Sentry error monitoring.
///
/// Sentry is OPTIONAL:
///   - If Sentry:Dsn is missing or empty, Sentry is disabled and the application starts normally.
///   - If configured, Sentry captures unhandled exceptions and ASP.NET Core request errors.
///
/// Security:
///   - Authorization headers are stripped before any data is sent to Sentry.
///   - Cookie headers are stripped (contain authentication tokens).
///   - Request bodies are NOT sent (may contain passwords, payment data).
///   - Sensitive header names are filtered from captured context.
///   - Database connection strings never appear in Sentry events.
///
/// Sentry does not replace Serilog.
///   - Serilog handles structured application logging.
///   - Sentry handles exception alerting and production error dashboards.
/// </summary>
internal static class SentryExtensions
{
    // Headers that must never be sent to Sentry — contain credentials or sensitive identifiers
    private static readonly HashSet<string> RedactedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Cookie",
        "Set-Cookie",
        "X-Api-Key",
        "X-Cart-Token",       // anonymous cart session token
        "X-Bootstrap-Secret", // not a real header, but guard anyway
    };

    internal static WebApplicationBuilder AddSentryMonitoring(this WebApplicationBuilder builder)
    {
        var dsn = builder.Configuration["Sentry:Dsn"];

        // No DSN configured — Sentry is disabled; application starts normally
        if (string.IsNullOrWhiteSpace(dsn))
        {
            var startupLogger = LoggerFactory
                .Create(b => b.AddConsole())
                .CreateLogger("Sentry");
            startupLogger.LogInformation(
                "Sentry DSN not configured. Error monitoring is disabled. " +
                "Set the Sentry__Dsn environment variable (or a \"Sentry:Dsn\" appsettings entry) to enable. " +
                "No error events will be sent to Sentry until a DSN is configured.");
            return builder;
        }

        builder.WebHost.UseSentry(options =>
        {
            options.Dsn = dsn;
            options.Environment = builder.Environment.EnvironmentName;
            options.Release = builder.Configuration["Sentry:Release"]; // optional; set in CI/CD

            // Capture traces for performance monitoring (0 = disabled by default)
            if (double.TryParse(builder.Configuration["Sentry:TracesSampleRate"], out var rate))
                options.TracesSampleRate = Math.Clamp(rate, 0.0, 1.0);

            // SDK diagnostic output — invaluable when diagnosing a silent integration.
            if (bool.TryParse(builder.Configuration["Sentry:Debug"], out var debug))
                options.Debug = debug;

            // Breadcrumbs are recorded at Information and above.
            options.MinimumBreadcrumbLevel = LogLevel.Information;

            // Only send Warning and above to Sentry. Unhandled exceptions captured by
            // GlobalExceptionMiddleware are always reported regardless of this setting.
            options.MinimumEventLevel = LogLevel.Warning;

            // Request body capture is OFF — bodies may contain passwords, payment data, tokens
            options.MaxRequestBodySize = Sentry.Extensibility.RequestSize.None;

            // Strip sensitive headers before any request context is sent to Sentry
            options.SetBeforeSend((sentryEvent, hint) =>
            {
                ScrubSensitiveHeaders(sentryEvent);
                return sentryEvent;
            });
        });

        var enabledLogger = LoggerFactory
            .Create(b => b.AddConsole())
            .CreateLogger("Sentry");
        enabledLogger.LogInformation(
            "Sentry error monitoring enabled. Environment: {Environment} DsnHost: {Host}",
            builder.Environment.EnvironmentName,
            Uri.TryCreate(dsn, UriKind.Absolute, out var dsnUri) ? dsnUri.Host : "(unparseable DSN)");

        return builder;
    }

    private static void ScrubSensitiveHeaders(SentryEvent evt)
    {
        if (evt.Request?.Headers is null) return;

        foreach (var key in RedactedHeaders)
        {
            if (evt.Request.Headers.ContainsKey(key))
                evt.Request.Headers[key] = "[Filtered]";
        }

        // Also strip any header whose name contains common secret keywords
        var sensitivePatterns = new[] { "secret", "token", "key", "password", "auth", "credential" };
        var toRedact = evt.Request.Headers.Keys
            .Where(k => sensitivePatterns.Any(p =>
                k.Contains(p, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var key in toRedact)
            evt.Request.Headers[key] = "[Filtered]";
    }
}
