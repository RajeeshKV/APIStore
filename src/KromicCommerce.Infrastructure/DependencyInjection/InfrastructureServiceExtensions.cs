using System.Text;
using KromicCommerce.Application.Abstractions.Auth;
using KromicCommerce.Application.Abstractions.Catalog;
using KromicCommerce.Application.Abstractions.Commerce;
using KromicCommerce.Application.Abstractions.Data;
using KromicCommerce.Application.Abstractions.Email;
using KromicCommerce.Application.Abstractions.Media;
using KromicCommerce.Application.Abstractions.Payments;
using KromicCommerce.Application.Abstractions.Security;
using KromicCommerce.Application.Abstractions.Sms;
using KromicCommerce.Application.Abstractions.Store;
using KromicCommerce.Application.Options;
using KromicCommerce.Infrastructure.Auth;
using KromicCommerce.Infrastructure.BackgroundServices;
using KromicCommerce.Infrastructure.Caching;
using KromicCommerce.Infrastructure.Catalog;
using KromicCommerce.Infrastructure.Commerce;
using KromicCommerce.Infrastructure.Configuration;
using KromicCommerce.Infrastructure.Email;
using KromicCommerce.Infrastructure.Media;
using KromicCommerce.Infrastructure.Payments;
using KromicCommerce.Infrastructure.Persistence;
using KromicCommerce.Infrastructure.Security;
using KromicCommerce.Infrastructure.Sms;
using KromicCommerce.Infrastructure.Store;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;

namespace KromicCommerce.Infrastructure.DependencyInjection;

public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions(configuration)
            .AddPersistence(configuration)
            .AddCaching(configuration)
            .AddAuthServices(configuration)
            .AddSmsServices(configuration)
            .AddStoreServices()
            .AddCatalogServices()
            .AddPaymentServices()
            .AddEmailServices()
            .AddSecurityServices(configuration)
            .AddBackgroundServices()
            .AddHttpContextAccessor();

        return services;
    }

    // -------------------------------------------------------------------------
    // Options registration + startup validation
    // -------------------------------------------------------------------------
    private static IServiceCollection AddOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddAndValidate<DatabaseOptions>(configuration, DatabaseOptions.SectionName);
        services.AddAndValidate<JwtOptions>(configuration, JwtOptions.SectionName);
        services.AddAndValidate<CloudinaryOptions>(configuration, CloudinaryOptions.SectionName);
        services.AddAndValidate<CorsOptions>(configuration, CorsOptions.SectionName);
        services.AddAndValidate<RateLimitOptions>(configuration, RateLimitOptions.SectionName);
        services.AddAndValidate<CacheOptions>(configuration, CacheOptions.SectionName);
        services.AddAndValidate<BackgroundWorkerOptions>(configuration, BackgroundWorkerOptions.SectionName);
        services.AddAndValidate<WebhookOptions>(configuration, WebhookOptions.SectionName);
        services.AddAndValidate<AppOptions>(configuration, AppOptions.SectionName);

        // Tracking is optional — no startup validation, defaults to Manual provider
        services.AddOptions<TrackingOptions>().Bind(configuration.GetSection(TrackingOptions.SectionName));

        // Customer-configurable providers — NOT validated at startup (must not prevent app from starting)
        services.AddOptions<RazorpayOptions>().Bind(configuration.GetSection(RazorpayOptions.SectionName));
        services.AddOptions<GoogleOAuthOptions>().Bind(configuration.GetSection(GoogleOAuthOptions.SectionName));
        services.AddOptions<SmsOptions>().Bind(configuration.GetSection(SmsOptions.SectionName));
        services.AddOptions<BrevoOptions>().Bind(configuration.GetSection(BrevoOptions.SectionName));

        // Bridge JwtOptions → Application AuthTokenOptions (no Infrastructure dep in Application)
        services.AddOptions<AuthTokenOptions>().Configure<IOptions<JwtOptions>>((ato, jwt) =>
        {
            ato.AccessTokenExpiryMinutes = jwt.Value.AccessTokenExpiryMinutes;
            ato.RefreshTokenExpiryDays = jwt.Value.RefreshTokenExpiryDays;
        });

        // Bridge AppOptions.BootstrapSecret → Application BootstrapOptions
        services.AddOptions<BootstrapOptions>().Configure<IOptions<AppOptions>>((bo, app) =>
        {
            bo.BootstrapSecret = app.Value.BootstrapSecret;
        });

        // Bridge AppOptions.ApiBaseUrl → Application AppPublicOptions
        // Handlers use this to compute Google redirect URI and Razorpay webhook URL
        // without taking a direct dependency on Infrastructure.Configuration.
        services.AddOptions<AppPublicOptions>().Configure<IOptions<AppOptions>>((apo, app) =>
        {
            apo.ApiBaseUrl = app.Value.ApiBaseUrl;
            apo.FrontendUrl = app.Value.FrontendUrl;
        });

        // Bridge CacheOptions → Application CatalogCacheOptions
        services.AddOptions<CatalogCacheOptions>().Configure<IOptions<CacheOptions>>((cco, co) =>
        {
            cco.DefaultExpiryMinutes = co.Value.DefaultExpiryMinutes;
            cco.ShortExpiryMinutes = co.Value.ShortExpiryMinutes;
        });

        // Bridge Infrastructure provider options → Application status-only options (no secrets cross the boundary)
        services.AddOptions<RazorpayStatusOptions>().Configure<IOptions<RazorpayOptions>>((s, o) =>
        {
            s.Enabled = o.Value.Enabled;
            s.KeyId = o.Value.KeyId;
            s.HasKeySecret = !string.IsNullOrWhiteSpace(o.Value.KeySecret);
            s.HasWebhookSecret = !string.IsNullOrWhiteSpace(o.Value.WebhookSecret);
        });

        services.AddOptions<GoogleOAuthStatusOptions>().Configure<IOptions<GoogleOAuthOptions>>((s, o) =>
        {
            s.Enabled = o.Value.Enabled;
            s.ClientId = o.Value.ClientId;
            s.HasClientSecret = !string.IsNullOrWhiteSpace(o.Value.ClientSecret);
            s.RedirectUri = o.Value.RedirectUri;
        });

        services.AddOptions<BrevoStatusOptions>().Configure<IOptions<BrevoOptions>>((s, o) =>
        {
            s.Enabled = o.Value.Enabled;
            s.HasApiKey = !string.IsNullOrWhiteSpace(o.Value.ApiKey);
            s.SenderEmail = o.Value.SenderEmail;
            s.SenderName = o.Value.SenderName;
        });

        // Bridge TrackingOptions → Application TrackingPublicOptions (browser-safe IDs only)
        services.AddOptions<TrackingPublicOptions>().Configure<IOptions<TrackingOptions>>((t, o) =>
        {
            t.GoogleAnalyticsMeasurementId = string.IsNullOrWhiteSpace(o.Value.GoogleAnalyticsMeasurementId)
                ? null : o.Value.GoogleAnalyticsMeasurementId;
            t.MetaPixelId = string.IsNullOrWhiteSpace(o.Value.MetaPixelId)
                ? null : o.Value.MetaPixelId;
        });

        return services;
    }

    private static IServiceCollection AddAndValidate<TOptions>(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName)
        where TOptions : class
    {
        services
            .AddOptions<TOptions>()
            .Bind(configuration.GetSection(sectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        return services;
    }

    // -------------------------------------------------------------------------
    // EF Core / Npgsql
    // -------------------------------------------------------------------------
    private static IServiceCollection AddPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var dbOptions = configuration
            .GetSection(DatabaseOptions.SectionName)
            .Get<DatabaseOptions>()
            ?? throw new InvalidOperationException(
                $"Configuration section '{DatabaseOptions.SectionName}' is missing.");

        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseNpgsql(dbOptions.ConnectionString, npgsql =>
            {
                npgsql.EnableRetryOnFailure(dbOptions.MaxRetryCount);
                npgsql.CommandTimeout(dbOptions.CommandTimeoutSeconds);
            });
            if (dbOptions.EnableDetailedErrors) options.EnableDetailedErrors();
            if (dbOptions.EnableSensitiveDataLogging) options.EnableSensitiveDataLogging();
        });

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        return services;
    }

    // -------------------------------------------------------------------------
    // IMemoryCache
    // -------------------------------------------------------------------------
    private static IServiceCollection AddCaching(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var cacheOptions = configuration
            .GetSection(CacheOptions.SectionName)
            .Get<CacheOptions>() ?? new CacheOptions();

        services.AddMemoryCache(o => o.SizeLimit = cacheOptions.SizeLimit);
        return services;
    }

    // -------------------------------------------------------------------------
    // Auth services + JWT Bearer
    // -------------------------------------------------------------------------
    private static IServiceCollection AddAuthServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var jwtSection = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? throw new InvalidOperationException("JWT configuration is missing.");

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtSection.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtSection.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtSection.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "sub",
                    RoleClaimType = System.Security.Claims.ClaimTypes.Role
                };

                options.Events = new JwtBearerEvents
                {
                    OnChallenge = ctx =>
                    {
                        // Return a clean JSON 401 rather than the default WWW-Authenticate redirect
                        ctx.HandleResponse();
                        ctx.Response.StatusCode = 401;
                        ctx.Response.ContentType = "application/json";
                        return ctx.Response.WriteAsync(
                            """{"success":false,"error":{"code":"UNAUTHORIZED","message":"Authentication required."}}""");
                    },
                    OnForbidden = ctx =>
                    {
                        ctx.Response.StatusCode = 403;
                        ctx.Response.ContentType = "application/json";
                        return ctx.Response.WriteAsync(
                            """{"success":false,"error":{"code":"FORBIDDEN","message":"You do not have permission to perform this action."}}""");
                    }
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("AdminOnly", policy =>
                policy.RequireRole(nameof(UserRole.Admin)));
            options.AddPolicy("CustomerOrAdmin", policy =>
                policy.RequireRole(nameof(UserRole.Customer), nameof(UserRole.Admin)));
        });

        // Application-layer auth services
        services.AddScoped<IJwtService, JwtService>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();
        services.AddScoped<IPasswordService, PasswordService>();
        services.AddScoped<IGoogleAuthService, GoogleAuthService>();
        services.AddScoped<IOtpService, OtpService>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        return services;
    }

    // -------------------------------------------------------------------------
    // SMS provider
    // -------------------------------------------------------------------------
    private static IServiceCollection AddSmsServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // SMS is customer-configurable and must never block startup, so nothing here uses
        // ValidateOnStart — an incomplete configuration is reported through
        // ISmsProviderFactory.Status instead of crashing the host.
        services.AddOptions<SmsOptions>().Bind(configuration.GetSection(SmsOptions.SectionName));
        services.AddOptions<SmsOtpPolicyOptions>()
            .Bind(configuration.GetSection($"{SmsOptions.SectionName}:{nameof(SmsOptions.Otp)}"));

        var section = configuration.GetSection(SmsOptions.SectionName);
        services.AddOptions<TwoFactorOptions>().Bind(section.GetSection(nameof(SmsOptions.TwoFactor)));
        services.AddOptions<Free2SmsOptions>().Bind(section.GetSection(nameof(SmsOptions.Free2Sms)));
        services.AddOptions<TwilioOptions>().Bind(section.GetSection(nameof(SmsOptions.Twilio)));

        // Per-provider timeouts. An OTP is useless if it arrives minutes late, so the
        // gateway budget is deliberately short.
        AddSmsHttpClient(services, TwoFactorProvider.HttpClientName);
        AddSmsHttpClient(services, Free2SmsProvider.HttpClientName);
        AddSmsHttpClient(services, TwilioProvider.HttpClientName);

        // Scoped, not singleton: the adapters look up their message template and the
        // administrator's saved credentials per send.
        services.AddScoped<ISmsTemplateStore, EfSmsTemplateStore>();
        services.AddScoped<ISmsProviderSettings, DbSmsProviderSettings>();
        services.AddScoped<ISmsProviderFactory, SmsProviderFactory>();

        // Bridge SMS policy → Application SmsPolicyOptions (policy values only, no credentials)
        services.AddOptions<SmsPolicyOptions>().Configure<IOptions<SmsOptions>>((s, o) =>
        {
            s.ExpiryMinutes = o.Value.Otp.ClampedExpiryMinutes;
            s.ResendCooldownSeconds = o.Value.Otp.ClampedResendCooldownSeconds;
            s.MaxAttempts = o.Value.Otp.ClampedMaxAttempts;
            s.Length = o.Value.Otp.ClampedLength;
            s.RequireVerifiedPhoneAtCheckout = o.Value.RequireVerifiedPhoneAtCheckout;
        });

        return services;
    }

    private static void AddSmsHttpClient(IServiceCollection services, string name)
        => services.AddHttpClient(name, client => client.Timeout = TimeSpan.FromSeconds(15));

    // -------------------------------------------------------------------------
    // Store services
    // -------------------------------------------------------------------------
    private static IServiceCollection AddStoreServices(this IServiceCollection services)
    {
        services.AddScoped<IBusinessSettingsService, BusinessSettingsService>();
        services.AddScoped<IPromotionService, PromotionService>();
        return services;
    }

    // -------------------------------------------------------------------------
    // Catalog services
    // -------------------------------------------------------------------------
    private static IServiceCollection AddCatalogServices(this IServiceCollection services)
    {
        services.AddScoped<ICatalogCacheService, CatalogCacheService>();
        services.AddScoped<ICloudinaryService, CloudinaryService>();
        return services;
    }

    // -------------------------------------------------------------------------
    // Payment services
    // -------------------------------------------------------------------------
    private static IServiceCollection AddPaymentServices(this IServiceCollection services)
    {
        services.AddScoped<IPaymentGateway, RazorpayPaymentGateway>();
        return services;
    }

    // -------------------------------------------------------------------------
    // Email services
    // -------------------------------------------------------------------------
    private static IServiceCollection AddEmailServices(this IServiceCollection services)
    {
        services.AddHttpClient("Brevo");
        services.AddScoped<IEmailService, BrevoEmailService>();
        return services;
    }

    // -------------------------------------------------------------------------
    // Security services (secret protection, data protection)
    // -------------------------------------------------------------------------
    private static IServiceCollection AddSecurityServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDataProtection();

        var environmentKey = configuration[$"{SecretProtectionOptions.SectionName}:EncryptionKey"];
        if (string.IsNullOrWhiteSpace(environmentKey))
        {
            services.AddScoped<ISecretProtectionService, DataProtectionSecretService>();
            return services;
        }

        services.AddOptions<SecretProtectionOptions>()
            .Bind(configuration.GetSection(SecretProtectionOptions.SectionName))
            .Validate(
                options => IsValidEnvironmentKey(options.EncryptionKey),
                "SecretProtection:EncryptionKey must be a base64-encoded 32-byte key.")
            .ValidateOnStart();
        services.AddScoped<ISecretProtectionService, EnvironmentKeySecretProtectionService>();
        return services;
    }

    private static bool IsValidEnvironmentKey(string key)
    {
        try { return Convert.FromBase64String(key).Length == 32; }
        catch (FormatException) { return false; }
    }

    // -------------------------------------------------------------------------
    // Background services
    // -------------------------------------------------------------------------
    private static IServiceCollection AddBackgroundServices(this IServiceCollection services)
    {
        services.AddHostedService<OutboxProcessor>();
        return services;
    }
}
