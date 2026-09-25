namespace KromicCommerce.Infrastructure.Configuration;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    [Required(AllowEmptyStrings = false, ErrorMessage = "Database:ConnectionString is required.")]
    public string ConnectionString { get; init; } = string.Empty;

    /// <summary>Max retry count for transient PostgreSQL errors. Default: 3.</summary>
    public int MaxRetryCount { get; init; } = 3;

    /// <summary>Command timeout in seconds. Default: 30.</summary>
    public int CommandTimeoutSeconds { get; init; } = 30;

    /// <summary>Enable EF Core detailed errors in development. Never enable in production.</summary>
    public bool EnableDetailedErrors { get; init; } = false;

    /// <summary>Enable EF Core sensitive data logging. Never enable in production.</summary>
    public bool EnableSensitiveDataLogging { get; init; } = false;
}
