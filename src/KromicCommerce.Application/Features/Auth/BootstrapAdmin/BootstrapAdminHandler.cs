using KromicCommerce.Application.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KromicCommerce.Application.Features.Auth.BootstrapAdmin;

internal sealed class BootstrapAdminHandler(
    IApplicationDbContext db,
    IPasswordService passwordService,
    IJwtService jwtService,
    IRefreshTokenService refreshTokenService,
    IOptions<AuthTokenOptions> tokenOptions,
    IOptions<BootstrapOptions> bootstrapOptions,
    ILogger<BootstrapAdminHandler> logger)
    : ICommandHandler<BootstrapAdminCommand, TokenResponse>
{
    public async Task<Result<TokenResponse>> Handle(
        BootstrapAdminCommand command,
        CancellationToken cancellationToken)
    {
        // Verify bootstrap secret — prevents unauthorized bootstrap
        var expectedSecret = bootstrapOptions.Value.BootstrapSecret;
        if (string.IsNullOrWhiteSpace(expectedSecret) ||
            !string.Equals(command.BootstrapSecret, expectedSecret, StringComparison.Ordinal))
        {
            logger.LogWarning("Bootstrap attempt with invalid secret.");
            return Result.Failure<TokenResponse>(
                Error.Forbidden("BOOTSTRAP_INVALID_SECRET", "Bootstrap secret is invalid."));
        }

        // One-time: reject if any admin already exists
        var adminExists = await db.Users
            .AnyAsync(u => u.Role == UserRole.Admin, cancellationToken);

        if (adminExists)
            return Result.Failure<TokenResponse>(
                Error.Conflict("BOOTSTRAP_ALREADY_DONE", "Admin already exists. Bootstrap is a one-time operation."));

        var passwordHash = passwordService.Hash(command.Password);
        var admin = User.CreateAdmin(command.Email, passwordHash, command.FirstName, command.LastName);

        // Set username if provided — enables login by username in addition to email
        if (!string.IsNullOrWhiteSpace(command.Username))
        {
            try { admin.SetUsername(command.Username); }
            catch (ArgumentException ex)
            {
                return Result.Failure<TokenResponse>(
                    Error.Validation("INVALID_USERNAME", ex.Message));
            }
        }

        var settings = BusinessSettings.CreateDefault(command.BusinessName);

        db.Users.Add(admin);
        db.BusinessSettings.Add(settings);

        var opts = tokenOptions.Value;
        var rawToken = refreshTokenService.GenerateRawToken();
        var tokenHash = refreshTokenService.HashToken(rawToken);
        var expiresAt = DateTime.UtcNow.AddDays(opts.RefreshTokenExpiryDays);
        db.RefreshTokens.Add(Domain.Identity.RefreshToken.Create(admin.Id, tokenHash, expiresAt));

        await db.SaveChangesAsync(cancellationToken);

        var accessToken = jwtService.IssueAccessToken(
            admin.Id, admin.Email, admin.Role.ToString(), admin.TokenVersion);

        logger.LogInformation("Admin bootstrapped: {UserId} for business '{Business}'",
            admin.Id, command.BusinessName);

        return Result.Success(new TokenResponse(
            AccessToken: accessToken,
            RefreshToken: rawToken,
            AccessTokenExpiresInSeconds: opts.AccessTokenExpiryMinutes * 60));
    }
}
