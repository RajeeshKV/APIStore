using Asp.Versioning.ApiExplorer;
using Microsoft.OpenApi.Models;

namespace KromicCommerce.Api.Extensions;

internal static class SwaggerExtensions
{
    internal static IServiceCollection AddSwaggerServices(this IServiceCollection services)
    {
        services.AddSwaggerGen(options =>
        {
            // Swagger docs are generated per API version discovered at runtime.
            // The provider is not available at registration time, so we use a placeholder
            // and generate real docs in ConfigureSwaggerDocs (called after Build()).
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Kromic Commerce API",
                Version = "v1",
                Description = "Kromic Commerce — isolated-deployment e-commerce platform API."
            });

            // Bearer token authentication scheme for Swagger UI
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Enter your JWT access token."
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    []
                }
            });

            // Include XML documentation comments when present
            var xmlFiles = Directory.GetFiles(AppContext.BaseDirectory, "*.xml");
            foreach (var xmlFile in xmlFiles)
                options.IncludeXmlComments(xmlFile, includeControllerXmlComments: true);
        });

        return services;
    }

    /// <summary>
    /// Registers Swagger UI endpoints for all discovered API versions.
    /// Must be called on the built WebApplication.
    /// </summary>
    internal static WebApplication UseSwaggerWithVersioning(this WebApplication app)
    {
        app.UseSwagger();

        var provider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();

        app.UseSwaggerUI(options =>
        {
            foreach (var description in provider.ApiVersionDescriptions)
            {
                options.SwaggerEndpoint(
                    $"/swagger/{description.GroupName}/swagger.json",
                    $"Kromic Commerce API {description.GroupName.ToUpperInvariant()}");
            }

            options.RoutePrefix = "swagger";
        });

        return app;
    }
}
