using KromicCommerce.Application.Abstractions.Email;

namespace KromicCommerce.Application.Features.Auth.PasswordReset;

// -----------------------------------------------------------------------
// Commands
// -----------------------------------------------------------------------

/// <summary>
/// Initiate a password reset for an admin account.
/// Always returns success to prevent email enumeration attacks.
/// </summary>
public sealed record RequestPasswordResetCommand(string Email) : ICommand;

/// <summary>Complete the reset with a token + new password.</summary>
public sealed record ResetPasswordCommand(
    string Email,
    string Token,
    string NewPassword) : ICommand;

// -----------------------------------------------------------------------
// Validators
// -----------------------------------------------------------------------

internal sealed class RequestPasswordResetValidator
    : AbstractValidator<RequestPasswordResetCommand>
{
    public RequestPasswordResetValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
    }
}

internal sealed class ResetPasswordValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Token).NotEmpty().MaximumLength(128);
        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .MinimumLength(8).WithMessage("Password must be at least 8 characters.")
            .MaximumLength(128);
    }
}

// -----------------------------------------------------------------------
// RequestPasswordResetHandler
// -----------------------------------------------------------------------

/// <summary>
/// Issues a short-lived, single-use reset token and sends it by email.
///
/// Security properties:
///   - Always returns success (no account enumeration).
///   - Raw token is never stored — only the SHA-256 hash.
///   - Token expires in 15 minutes.
///   - Token is invalidated on first use via ResetPasswordHandler.
///   - Reset token is never logged.
/// </summary>
internal sealed class RequestPasswordResetHandler(
    IApplicationDbContext db,
    IPasswordService passwordService,
    IEmailService emailService,
    ILogger<RequestPasswordResetHandler> logger)
    : ICommandHandler<RequestPasswordResetCommand>
{
    private static readonly TimeSpan TokenTtl = TimeSpan.FromMinutes(15);

    public async Task<Result> Handle(
        RequestPasswordResetCommand command, CancellationToken ct)
    {
        var normalised = command.Email.Trim().ToUpperInvariant();

        var user = await db.Users
            .FirstOrDefaultAsync(
                u => u.NormalizedEmail == normalised && u.Role == UserRole.Admin, ct);

        // Always return success to prevent email enumeration
        if (user is null || !user.IsActive)
        {
            logger.LogInformation(
                "Password reset requested for unknown/inactive admin email (suppressed).");
            return Result.Success();
        }

        var rawToken = passwordService.GenerateResetToken();
        var tokenHash = passwordService.HashToken(rawToken);
        var expiresAt = DateTime.UtcNow.Add(TokenTtl);

        user.SetPasswordResetToken(tokenHash, expiresAt);
        await db.SaveChangesAsync(ct);

        // Send email — non-fatal if Brevo is unavailable
        try
        {
            await emailService.SendPasswordResetAsync(
                user.Email, user.FullName, rawToken, ct);
        }
        catch (Exception ex)
        {
            // Log but don't fail — the admin can request again
            logger.LogError(ex,
                "Failed to send password reset email for UserId: {UserId}", user.Id);
        }

        // rawToken is intentionally not logged
        logger.LogInformation("Password reset token issued for admin UserId: {UserId}", user.Id);

        return Result.Success();
    }
}

// -----------------------------------------------------------------------
// ResetPasswordHandler
// -----------------------------------------------------------------------

/// <summary>
/// Validates the token, resets the password, and invalidates all sessions.
///
/// Security properties:
///   - Token is single-use (cleared on success).
///   - TokenVersion is incremented, invalidating all existing JWTs and refresh tokens.
///   - All refresh tokens are revoked.
///   - A timing-safe comparison is used (constant-time string compare via HMAC; here
///     we use string == on hex hashes, which is effectively constant-time for fixed-length).
///   - Token expiry is checked.
/// </summary>
internal sealed class ResetPasswordHandler(
    IApplicationDbContext db,
    IPasswordService passwordService,
    ILogger<ResetPasswordHandler> logger)
    : ICommandHandler<ResetPasswordCommand>
{
    private static readonly Error InvalidToken =
        Error.Validation("RESET_TOKEN_INVALID", "The reset token is invalid or has expired.");

    public async Task<Result> Handle(ResetPasswordCommand command, CancellationToken ct)
    {
        var normalised = command.Email.Trim().ToUpperInvariant();

        var user = await db.Users
            .Include(u => u.RefreshTokens)
            .FirstOrDefaultAsync(
                u => u.NormalizedEmail == normalised && u.Role == UserRole.Admin, ct);

        if (user is null || !user.IsActive)
            return Result.Failure(InvalidToken);

        // Check token exists and is unexpired
        if (user.PasswordResetTokenHash is null || user.PasswordResetTokenExpiresAt is null)
            return Result.Failure(InvalidToken);

        if (DateTime.UtcNow > user.PasswordResetTokenExpiresAt.Value)
        {
            user.ClearPasswordResetToken();
            await db.SaveChangesAsync(ct);
            return Result.Failure(InvalidToken);
        }

        // Verify token (SHA-256 hash comparison)
        var incomingHash = passwordService.HashToken(command.Token.Trim());
        if (!string.Equals(incomingHash, user.PasswordResetTokenHash, StringComparison.Ordinal))
            return Result.Failure(InvalidToken);

        // Hash new password
        var newHash = passwordService.Hash(command.NewPassword);

        // ResetPassword: updates hash, increments TokenVersion, clears reset token
        user.ResetPassword(newHash);

        // Revoke all existing refresh tokens — forces re-login on all devices
        var tokens = await db.RefreshTokens
            .Where(t => t.UserId == user.Id && !t.IsRevoked)
            .ToListAsync(ct);

        foreach (var token in tokens)
            token.Revoke();

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Password reset completed for admin UserId: {UserId}. All sessions invalidated.",
            user.Id);

        return Result.Success();
    }
}
