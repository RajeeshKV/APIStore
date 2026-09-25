namespace KromicCommerce.Application.Options;

/// <summary>
/// Token expiry configuration consumed by Application layer handlers.
/// Infrastructure binds the values from JwtOptions and registers this type.
/// This avoids Application taking a direct dependency on Infrastructure.Configuration.
/// </summary>
public sealed class AuthTokenOptions
{
    public int AccessTokenExpiryMinutes { get; set; } = 15;
    public int RefreshTokenExpiryDays { get; set; } = 30;
}
