using KromicCommerce.Application.Options;
using Microsoft.EntityFrameworkCore;

namespace KromicCommerce.Application.Features.Auth.GoogleCallback;

internal sealed class GoogleCallbackHandler(
    IApplicationDbContext db,
    IBusinessSettingsService businessSettings,
    IGoogleAuthService googleAuth,
    IJwtService jwtService,
    IRefreshTokenService refreshTokenService,
    IOptions<AuthTokenOptions> tokenOptions,
    ILogger<GoogleCallbackHandler> logger)
    : ICommandHandler<GoogleCallbackCommand, TokenResponse>
{
    public async Task<Result<TokenResponse>> Handle(
        GoogleCallbackCommand command,
        CancellationToken cancellationToken)
    {
        // -----------------------------------------------------------------------
        // Load Google credentials from the database — not from environment variables.
        // This ensures credentials saved via Admin UI are used immediately.
        // -----------------------------------------------------------------------
        var settings = await businessSettings.GetAsync(cancellationToken);
        var auth = settings?.Auth;

        if (auth is null || !auth.IsGoogleOAuthConfigured)
            return Result.Failure<TokenResponse>(
                Error.Validation("AUTH_GOOGLE_NOT_CONFIGURED",
                    "Google authentication is not configured for this store."));

        if (!auth.GoogleOAuthEnabled)
            return Result.Failure<TokenResponse>(
                Error.Validation("AUTH_GOOGLE_DISABLED",
                    "Google authentication is disabled."));

        // Validate Google ID token against the DB-sourced Client ID
        var identity = await googleAuth.ValidateIdTokenAsync(
            command.IdToken, auth.GoogleClientId!, cancellationToken);

        if (identity is null)
            return Result.Failure<TokenResponse>(
                Error.Unauthorized("AUTH_GOOGLE_INVALID", "Google authentication failed."));

        // -----------------------------------------------------------------------
        // Look up existing external login by provider + subject (not email)
        // -----------------------------------------------------------------------
        var externalLogin = await db.ExternalLogins
            .Include(el => el.User)
            .FirstOrDefaultAsync(
                el => el.Provider == "Google" && el.ProviderSubject == identity.Subject,
                cancellationToken);

        User user;

        if (externalLogin is not null)
        {
            user = externalLogin.User;
            if (!user.IsActive)
                return Result.Failure<TokenResponse>(
                    Error.Forbidden("AUTH_ACCOUNT_INACTIVE", "This account is deactivated."));
        }
        else
        {
            // Check if a local account exists with the same verified email — link rather than duplicate
            user = await db.Users
                .FirstOrDefaultAsync(
                    u => u.NormalizedEmail == identity.Email.Trim().ToUpperInvariant(),
                    cancellationToken)
                ?? User.CreateCustomer(
                    identity.Email,
                    passwordHash: null,        // Google-only account has no password
                    identity.GivenName,
                    identity.FamilyName);

            if (identity.EmailVerified)
                user.MarkEmailVerified();

            var login = ExternalLogin.Create(user.Id, "Google", identity.Subject, identity.Email);
            user.AddExternalLogin(login);

            var isNew = user.DomainEvents.OfType<UserRegisteredEvent>().Any();
            if (isNew)
            {
                db.Users.Add(user);
                db.CustomerProfiles.Add(CustomerProfile.Create(user.Id));
            }

            db.ExternalLogins.Add(login);
        }

        user.RecordLogin();

        var opts = tokenOptions.Value;
        var rawToken = refreshTokenService.GenerateRawToken();
        var tokenHash = refreshTokenService.HashToken(rawToken);
        var expiresAt = DateTime.UtcNow.AddDays(opts.RefreshTokenExpiryDays);
        db.RefreshTokens.Add(Domain.Identity.RefreshToken.Create(
            user.Id, tokenHash, expiresAt, command.DeviceHint));

        await db.SaveChangesAsync(cancellationToken);

        var accessToken = jwtService.IssueAccessToken(
            user.Id, user.Email, user.Role.ToString(), user.TokenVersion);

        logger.LogInformation("Google OAuth login succeeded. UserId: {UserId}", user.Id);

        return Result.Success(new TokenResponse(
            AccessToken: accessToken,
            RefreshToken: rawToken,
            AccessTokenExpiresInSeconds: opts.AccessTokenExpiryMinutes * 60));
    }
}
