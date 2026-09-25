using KromicCommerce.Application.Options;
using Microsoft.EntityFrameworkCore;

namespace KromicCommerce.Application.Features.Auth.RegisterCustomer;

internal sealed class RegisterCustomerHandler(
    IApplicationDbContext db,
    IPasswordService passwordService,
    IJwtService jwtService,
    IRefreshTokenService refreshTokenService,
    IOptions<AuthTokenOptions> tokenOptions,
    ILogger<RegisterCustomerHandler> logger)
    : ICommandHandler<RegisterCustomerCommand, TokenResponse>
{
    public async Task<Result<TokenResponse>> Handle(
        RegisterCustomerCommand command,
        CancellationToken cancellationToken)
    {
        var exists = await db.Users
            .AnyAsync(u => u.NormalizedEmail == command.Email.Trim().ToUpperInvariant(),
                cancellationToken);

        if (exists)
            return Result.Failure<TokenResponse>(
                Error.Conflict("AUTH_EMAIL_TAKEN", "An account with this email already exists."));

        var passwordHash = passwordService.Hash(command.Password);
        var user = User.CreateCustomer(command.Email, passwordHash, command.FirstName, command.LastName);

        if (!string.IsNullOrWhiteSpace(command.PhoneNumber))
            user.SetPhoneNumber(command.PhoneNumber, verified: false);

        var profile = CustomerProfile.Create(user.Id);

        db.Users.Add(user);
        db.CustomerProfiles.Add(profile);

        var rawToken = refreshTokenService.GenerateRawToken();
        var tokenHash = refreshTokenService.HashToken(rawToken);
        var opts = tokenOptions.Value;
        var expiresAt = DateTime.UtcNow.AddDays(opts.RefreshTokenExpiryDays);
        var refreshToken = Domain.Identity.RefreshToken.Create(user.Id, tokenHash, expiresAt, command.DeviceHint);
        db.RefreshTokens.Add(refreshToken);

        await db.SaveChangesAsync(cancellationToken);

        var accessToken = jwtService.IssueAccessToken(
            user.Id, user.Email, user.Role.ToString(), user.TokenVersion);

        logger.LogInformation("Customer registered: {UserId}", user.Id);

        return Result.Success(new TokenResponse(
            AccessToken: accessToken,
            RefreshToken: rawToken,
            AccessTokenExpiresInSeconds: opts.AccessTokenExpiryMinutes * 60));
    }
}
