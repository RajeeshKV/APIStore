using Google.Apis.Auth;
using KromicCommerce.Application.Abstractions.Auth;

namespace KromicCommerce.Infrastructure.Auth;

/// <summary>
/// Validates Google ID tokens using the official Google.Apis.Auth library.
/// The clientId is supplied by the caller (loaded from BusinessSettings at runtime)
/// rather than from environment-variable-backed IOptions, so credential changes
/// made through the Admin UI are picked up immediately without a restart.
/// </summary>
internal sealed class GoogleAuthService(ILogger<GoogleAuthService> logger) : IGoogleAuthService
{
    public async Task<GoogleIdentity?> ValidateIdTokenAsync(
        string idToken,
        string clientId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            logger.LogWarning("Google ID token validation skipped — ClientId not configured.");
            return null;
        }

        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = [clientId]
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
