using KromicCommerce.Infrastructure.Configuration;

namespace KromicCommerce.Api.Extensions;

internal static class CorsExtensions
{
    internal static IServiceCollection AddCorsPolicy(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var allowedOrigins = configuration
            .GetSection(CorsOptions.SectionName)
            .Get<CorsOptions>()
            ?.AllowedOrigins ?? [];

        services.AddCors(options =>
        {
            options.AddPolicy(CorsOptions.PolicyName, policy =>
            {
                if (allowedOrigins.Length > 0)
                {
                    policy
                        .WithOrigins(allowedOrigins)
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials(); // needed for SignalR in later phases
                }
                else
                {
                    // No configured origins — block all cross-origin requests.
                    // This is intentional; fail loudly rather than fall back to a wildcard.
                    policy.SetIsOriginAllowed(_ => false);
                }
            });
        });

        return services;
    }
}
