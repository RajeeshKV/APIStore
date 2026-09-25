using KromicCommerce.Application.Options;
using Microsoft.EntityFrameworkCore;

namespace KromicCommerce.Application.Features.Auth.RefreshToken;

internal sealed class RefreshTokenHandler(
    IApplicationDbContext db,
    IJwtService jwtService,
    IRefreshTokenService refreshTokenService,
    IOptions<AuthTokenOptions> tokenOptions,
    ILogger<RefreshTokenHandler> logger)
    : ICommandHandler<RefreshTokenCommand, TokenResponse>
{
    private static readonly Error InvalidToken =
        Error.Unauthorized("AUTH_INVALID_REFRESH_TOKEN", "The refresh token is invalid or expired.");

    public async Task<Result<TokenResponse>> Handle(
        RefreshTokenCommand command,
        CancellationToken cancellationToken)
    {
        var tokenHash = refreshTokenService.HashToken(command.RawRefreshToken);

        var stored = await db.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash, cancellationToken);

        if (stored is null)
            return Result.Failure<TokenResponse>(InvalidToken);

        // Reuse detection: token was already revoked — revoke the entire family
        if (stored.IsRevoked)
        {
            logger.LogWarning(
                "Refresh token reuse detected for User {UserId}. Revoking all tokens.",
                stored.UserId);
            await RevokeAllUserTokensAsync(stored.UserId, cancellationToken);
            return Result.Failure<TokenResponse>(InvalidToken);
        }

        if (stored.IsExpired)
            return Result.Failure<TokenResponse>(InvalidToken);

        var user = stored.User;
        if (!user.IsActive)
            return Result.Failure<TokenResponse>(InvalidToken);

        // Rotate: revoke old, issue new
        var opts = tokenOptions.Value;
        var rawNew = refreshTokenService.GenerateRawToken();
        var hashNew = refreshTokenService.HashToken(rawNew);
        var expiresAt = DateTime.UtcNow.AddDays(opts.RefreshTokenExpiryDays);
        var newToken = Domain.Identity.RefreshToken.Create(user.Id, hashNew, expiresAt, command.DeviceHint);

        db.RefreshTokens.Add(newToken);
        await db.SaveChangesAsync(cancellationToken); // get new token Id first

        stored.Revoke(replacedByTokenId: newToken.Id);
        await db.SaveChangesAsync(cancellationToken);

        var accessToken = jwtService.IssueAccessToken(
            user.Id, user.Email, user.Role.ToString(), user.TokenVersion);

        return Result.Success(new TokenResponse(
            AccessToken: accessToken,
            RefreshToken: rawNew,
            AccessTokenExpiresInSeconds: opts.AccessTokenExpiryMinutes * 60));
    }

    private async Task RevokeAllUserTokensAsync(Guid userId, CancellationToken cancellationToken)
    {
        var tokens = await db.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var t in tokens)
            t.Revoke();

        await db.SaveChangesAsync(cancellationToken);
    }
}
