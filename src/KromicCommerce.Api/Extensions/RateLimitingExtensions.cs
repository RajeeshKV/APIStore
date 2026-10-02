using System.Security.Claims;
using System.Threading.RateLimiting;
using KromicCommerce.Infrastructure.Configuration;

namespace KromicCommerce.Api.Extensions;

internal static class RateLimitingExtensions
{
    internal const string GeneralPolicy = "general";
    internal const string AuthPolicy = "auth";
    internal const string OtpPolicy = "otp";
    internal const string PasswordResetPolicy = "password-reset";
    internal const string MediaUploadPolicy = "media-upload";

    internal static IServiceCollection AddRateLimitingPolicies(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var opts = configuration
            .GetSection(RateLimitOptions.SectionName)
            .Get<RateLimitOptions>() ?? new RateLimitOptions();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // General API rate limit — keyed by client IP
            limiter.AddPolicy(GeneralPolicy, ctx =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetClientIp(ctx),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = opts.PermitLimit,
                        Window = TimeSpan.FromSeconds(opts.WindowSeconds),
                        QueueLimit = opts.QueueLimit
                    }));

            // Auth endpoints (login, register, refresh)
            limiter.AddPolicy(AuthPolicy, ctx =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetClientIp(ctx),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = opts.AuthPermitLimit,
                        Window = TimeSpan.FromSeconds(opts.AuthWindowSeconds),
                        QueueLimit = 0
                    }));

            // OTP endpoints
            limiter.AddPolicy(OtpPolicy, ctx =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetClientIp(ctx),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = opts.OtpPermitLimit,
                        Window = TimeSpan.FromSeconds(opts.OtpWindowSeconds),
                        QueueLimit = 0
                    }));

            // Password reset — stricter than auth; 3 requests per 15 minutes per IP
            limiter.AddPolicy(PasswordResetPolicy, ctx =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetClientIp(ctx),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 3,
                        Window = TimeSpan.FromMinutes(15),
                        QueueLimit = 0
                    }));

            // Customer review-image uploads.
            //
            // Keyed by authenticated user id, NOT by IP, on purpose. An IP-keyed quota punishes
            // everyone behind one NAT or office egress for a single abusive account, and lets one
            // account rotate addresses freely. Falling back to the IP only when the caller is
            // anonymous keeps the partition key stable.
            limiter.AddPolicy(MediaUploadPolicy, ctx =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetUserOrClientPartitionKey(ctx),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = opts.MediaUploadPermitLimit,
                        Window = TimeSpan.FromSeconds(opts.MediaUploadWindowSeconds),
                        QueueLimit = 0
                    }));
        });

        return services;
    }

    private static string GetClientIp(HttpContext ctx) =>
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    /// <summary>
    /// Partition key for per-account quotas: the authenticated subject when there is one,
    /// otherwise the client IP.
    ///
    /// The claim name is checked rather than assumed, because relying on a wrong name here would
    /// silently fall back to IP and quietly reintroduce the shared-quota problem.
    /// </summary>
    private static string GetUserOrClientPartitionKey(HttpContext ctx)
    {
        var subject = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? ctx.User.FindFirst("sub")?.Value;

        return string.IsNullOrWhiteSpace(subject)
            ? $"ip:{GetClientIp(ctx)}"
            : $"user:{subject}";
    }
}
