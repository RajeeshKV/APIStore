using Microsoft.EntityFrameworkCore;

namespace KromicCommerce.Application.Features.Auth.Logout;

internal sealed class LogoutHandler(
    IApplicationDbContext db,
    IRefreshTokenService refreshTokenService)
    : ICommandHandler<LogoutCommand>
{
    public async Task<Result> Handle(LogoutCommand command, CancellationToken cancellationToken)
    {
        var tokenHash = refreshTokenService.HashToken(command.RawRefreshToken);

        var token = await db.RefreshTokens
            .FirstOrDefaultAsync(
                rt => rt.TokenHash == tokenHash && rt.UserId == command.UserId,
                cancellationToken);

        if (token is not null && !token.IsRevoked)
        {
            token.Revoke();
            await db.SaveChangesAsync(cancellationToken);
        }

        // Always return success — logout is idempotent
        return Result.Success();
    }
}
