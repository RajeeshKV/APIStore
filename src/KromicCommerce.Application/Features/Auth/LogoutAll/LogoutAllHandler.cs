using Microsoft.EntityFrameworkCore;

namespace KromicCommerce.Application.Features.Auth.LogoutAll;

internal sealed class LogoutAllHandler(
    IApplicationDbContext db,
    ILogger<LogoutAllHandler> logger)
    : ICommandHandler<LogoutAllCommand>
{
    public async Task<Result> Handle(LogoutAllCommand command, CancellationToken cancellationToken)
    {
        // Increment TokenVersion → all existing JWTs immediately invalid
        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);

        if (user is null)
            return Result.Failure(Error.NotFound("USER_NOT_FOUND", "User not found."));

        user.IncrementTokenVersion();

        // Revoke all active refresh tokens
        var tokens = await db.RefreshTokens
            .Where(rt => rt.UserId == command.UserId && rt.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var t in tokens)
            t.Revoke();

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("LogoutAll executed for User {UserId}. {Count} tokens revoked.", command.UserId, tokens.Count);

        return Result.Success();
    }
}
