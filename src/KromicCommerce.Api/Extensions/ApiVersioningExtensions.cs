using Asp.Versioning;

namespace KromicCommerce.Api.Extensions;

internal static class ApiVersioningExtensions
{
    internal static IServiceCollection AddApiVersioningServices(this IServiceCollection services)
    {
        services
            .AddApiVersioning(options =>
            {
                options.DefaultApiVersion = new ApiVersion(1, 0);
                options.AssumeDefaultVersionWhenUnspecified = true;
                options.ReportApiVersions = true;
                // Support versioning via URL segment: /api/v1/...
                options.ApiVersionReader = new UrlSegmentApiVersionReader();
            })
            .AddApiExplorer(options =>
            {
                // Format version as 'v{major}' in group names
                options.GroupNameFormat = "'v'VVV";
                options.SubstituteApiVersionInUrl = true;
            });

        return services;
    }
}
