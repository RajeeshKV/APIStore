using KromicCommerce.Application.Options;
using Microsoft.EntityFrameworkCore;

namespace KromicCommerce.Application.Features.Auth.LoginWithEmail;

internal sealed class LoginWithEmailHandler(
    IApplicationDbContext db,
    IPasswordService passwordService,
    IJwtService jwtService,
    IRefreshTokenService refreshTokenService,
    IOptions<AuthTokenOptions> tokenOptions,
    ILogger<LoginWithEmailHandler> logger)
    : ICommandHandler<LoginWithEmailCommand, TokenResponse>
{
    // Generic error — never reveal whether email or password was wrong
    private static readonly Error InvalidCredentials =
        Error.Unauthorized("AUTH_INVALID_CREDENTIALS", "Invalid email or password.");

    public async Task<Result<TokenResponse>> Handle(
        LoginWithEmailCommand command,
        CancellationToken cancellationToken)
    {
        var input = command.Email.Trim();
        var normalised = input.ToUpperInvariant();

        // Support login by email OR username — check both normalised fields
        var user = await db.Users
            .FirstOrDefaultAsync(
                u => u.NormalizedEmail == normalised || u.NormalizedUsername == normalised,
                cancellationToken);

        if (user is null || !user.IsActive)
            return Result.Failure<TokenResponse>(InvalidCredentials);

        if (user.PasswordHash is null)
            // OAuth-only account — no password set
            return Result.Failure<TokenResponse>(InvalidCredentials);

        if (!passwordService.Verify(command.Password, user.PasswordHash))
            return Result.Failure<TokenResponse>(InvalidCredentials);

        user.RecordLogin();

        var opts = tokenOptions.Value;
        var rawToken = refreshTokenService.GenerateRawToken();
        var tokenHash = refreshTokenService.HashToken(rawToken);
        var expiresAt = DateTime.UtcNow.AddDays(opts.RefreshTokenExpiryDays);
        var refreshToken = Domain.Identity.RefreshToken.Create(user.Id, tokenHash, expiresAt, command.DeviceHint);
        db.RefreshTokens.Add(refreshToken);

        await db.SaveChangesAsync(cancellationToken);

        var accessToken = jwtService.IssueAccessToken(
            user.Id, user.Email, user.Role.ToString(), user.TokenVersion);

        logger.LogInformation("User logged in: {UserId}", user.Id);

        return Result.Success(new TokenResponse(
            AccessToken: accessToken,
            RefreshToken: rawToken,
            AccessTokenExpiresInSeconds: opts.AccessTokenExpiryMinutes * 60));
    }
}
