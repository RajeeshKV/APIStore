namespace KromicCommerce.Infrastructure.Configuration;

public sealed class AppOptions
{
    public const string SectionName = "App";

    /// <summary>Public-facing API base URL. Used in generated links and email templates.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "App:ApiBaseUrl is required.")]
    public string ApiBaseUrl { get; init; } = string.Empty;

    /// <summary>Frontend URL. Used in CORS validation and email links.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "App:FrontendUrl is required.")]
    public string FrontendUrl { get; init; } = string.Empty;

    /// <summary>Application environment. Used to gate development-only features.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "App:Environment is required.")]
    public string Environment { get; init; } = "Production";

    /// <summary>
    /// One-time admin bootstrap secret. Required before first admin is created.
    /// Must be a strong random value set via environment variable App__BootstrapSecret.
    /// After bootstrap is complete this value becomes inert (admin already exists check).
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "App:BootstrapSecret is required.")]
    public string BootstrapSecret { get; init; } = string.Empty;

    /// <summary>
    /// When true, the application runs EF Core Database.MigrateAsync() during startup
    /// before serving any API traffic.
    /// Set App__Migrate=true in production (Render environment variable).
    /// Defaults to false — migrations are intentionally off during local development
    /// to avoid accidental schema changes against shared databases.
    /// Never use EnsureCreated() in production.
    /// </summary>
    public bool Migrate { get; init; } = false;

    public bool IsProduction =>
        string.Equals(Environment, "Production", StringComparison.OrdinalIgnoreCase);

    public bool IsDevelopment =>
        string.Equals(Environment, "Development", StringComparison.OrdinalIgnoreCase);
}
