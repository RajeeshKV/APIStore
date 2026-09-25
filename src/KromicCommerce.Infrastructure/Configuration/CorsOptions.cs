namespace KromicCommerce.Infrastructure.Configuration;

public sealed class CorsOptions
{
    public const string SectionName = "Cors";
    public const string PolicyName = "KromicCorsPolicy";

    /// <summary>
    /// Allowed origins for the CORS policy.
    /// Must be explicitly configured — no wildcard is accepted in production.
    /// </summary>
    [Required(ErrorMessage = "Cors:AllowedOrigins must contain at least one origin.")]
    [MinLength(1, ErrorMessage = "Cors:AllowedOrigins must contain at least one origin.")]
    public string[] AllowedOrigins { get; init; } = [];
}
