using Google.Apis.Auth;
using KromicCommerce.Application.Abstractions.Auth;
using KromicCommerce.Infrastructure.Configuration;

namespace KromicCommerce.Infrastructure.Auth;

internal sealed class GoogleAuthService(
    IOptions<GoogleOAuthOptions> options,
    ILogger<GoogleAuthService> logger) : IGoogleAuthService
{
    public async Task<GoogleIdentity?> ValidateIdTokenAsync(
        string idToken,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = [options.Value.ClientId]
            };

            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);

            return new GoogleIdentity(
                Subject: payload.Subject,
                Email: payload.Email,
                EmailVerified: payload.EmailVerified,
                GivenName: payload.GivenName,
                FamilyName: payload.FamilyName,
                PictureUrl: payload.Picture);
        }
        catch (InvalidJwtException ex)
        {
            logger.LogWarning("Google ID token validation failed: {Message}", ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error validating Google ID token");
            return null;
        }
    }
}
