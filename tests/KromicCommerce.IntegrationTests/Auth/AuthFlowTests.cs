using KromicCommerce.Application.Features.Auth.LoginWithEmail;
using KromicCommerce.Application.Features.Auth.Logout;
using KromicCommerce.Application.Features.Auth.RefreshToken;
using KromicCommerce.Application.Features.Auth.RegisterCustomer;
using KromicCommerce.Application.Options;
using KromicCommerce.Infrastructure.Auth;
using KromicCommerce.Infrastructure.Configuration;
using KromicCommerce.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KromicCommerce.IntegrationTests.Auth;

/// <summary>
/// End-to-end auth flow tests against a real PostgreSQL database.
/// Tests: register → login → refresh → logout.
/// </summary>
[Collection("Database")]
public sealed class AuthFlowTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    private static readonly IOptions<AuthTokenOptions> TokenOpts =
        Options.Create(new AuthTokenOptions { AccessTokenExpiryMinutes = 15, RefreshTokenExpiryDays = 30 });

    private static readonly IOptions<JwtOptions> JwtOpts = Options.Create(new JwtOptions
    {
        SigningKey = "this-is-a-test-key-must-be-32chars!",
        Issuer = "test-issuer",
        Audience = "test-audience",
        AccessTokenExpiryMinutes = 15,
        RefreshTokenExpiryDays = 30
    });

    private JwtService JwtSvc => new(JwtOpts);
    private RefreshTokenService RtSvc => new();
    private PasswordService PwSvc => new();

    [SkippableFact]
    public async Task Register_login_refresh_logout_full_flow()
    {
        var ctx = Db.CreateDbContext();
        var email = $"flow_{Guid.NewGuid():N}@example.com";

        // 1. Register
        var registerHandler = new RegisterCustomerHandler(
            ctx, PwSvc, JwtSvc, RtSvc, TokenOpts,
            NullLogger<RegisterCustomerHandler>.Instance);

        var registerResult = await registerHandler.Handle(
            new RegisterCustomerCommand(email, "Password123!", "Test", "User", null, "device1"),
            CancellationToken.None);

        registerResult.IsSuccess.Should().BeTrue();
        var rawRefreshAfterRegister = registerResult.Value.RefreshToken;

        // 2. Login with same credentials
        await using var ctx2 = Db.CreateDbContext();
        var loginHandler = new LoginWithEmailHandler(
            ctx2, PwSvc, JwtSvc, RtSvc, TokenOpts,
            NullLogger<LoginWithEmailHandler>.Instance);

        var loginResult = await loginHandler.Handle(
            new LoginWithEmailCommand(email, "Password123!", "device1"),
            CancellationToken.None);

        loginResult.IsSuccess.Should().BeTrue();
        var rawRefreshAfterLogin = loginResult.Value.RefreshToken;

        // 3. Refresh
        await using var ctx3 = Db.CreateDbContext();
        var refreshHandler = new RefreshTokenHandler(
            ctx3, JwtSvc, RtSvc, TokenOpts,
            NullLogger<RefreshTokenHandler>.Instance);

        var refreshResult = await refreshHandler.Handle(
            new RefreshTokenCommand(rawRefreshAfterLogin, "device1"),
            CancellationToken.None);

        refreshResult.IsSuccess.Should().BeTrue();
        refreshResult.Value.AccessToken.Should().NotBeNullOrWhiteSpace();

        // Old token should now be revoked (rotation)
        var oldHash = RtSvc.HashToken(rawRefreshAfterLogin);
        var oldToken = await ctx3.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == oldHash);
        oldToken!.IsRevoked.Should().BeTrue();

        // 4. Logout
        var user = await ctx3.Users.FirstAsync(u => u.NormalizedEmail == email.ToUpperInvariant());
        await using var ctx4 = Db.CreateDbContext();
        var logoutHandler = new LogoutHandler(ctx4, RtSvc);

        var logoutResult = await logoutHandler.Handle(
            new LogoutCommand(refreshResult.Value.RefreshToken, user.Id),
            CancellationToken.None);

        logoutResult.IsSuccess.Should().BeTrue();

        // Verify new token is now revoked
        var newHash = RtSvc.HashToken(refreshResult.Value.RefreshToken);
        var newToken = await ctx4.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == newHash);
        newToken!.IsRevoked.Should().BeTrue();
    }

    [SkippableFact]
    public async Task Reuse_of_revoked_token_revokes_all_tokens()
    {
        var ctx = Db.CreateDbContext();
        var email = $"reuse_{Guid.NewGuid():N}@example.com";

        // Register to get a refresh token
        var registerHandler = new RegisterCustomerHandler(
            ctx, PwSvc, JwtSvc, RtSvc, TokenOpts,
            NullLogger<RegisterCustomerHandler>.Instance);

        var registerResult = await registerHandler.Handle(
            new RegisterCustomerCommand(email, "Password123!", "Reuse", "Test", null, null),
            CancellationToken.None);

        var rawToken = registerResult.Value.RefreshToken;

        // Use it once (rotates)
        await using var ctx2 = Db.CreateDbContext();
        var refreshHandler = new RefreshTokenHandler(
            ctx2, JwtSvc, RtSvc, TokenOpts,
            NullLogger<RefreshTokenHandler>.Instance);

        var firstRefresh = await refreshHandler.Handle(
            new RefreshTokenCommand(rawToken, null), CancellationToken.None);
        firstRefresh.IsSuccess.Should().BeTrue();

        // Reuse the original (now revoked) token — should fail and revoke all
        await using var ctx3 = Db.CreateDbContext();
        var reuseHandler = new RefreshTokenHandler(
            ctx3, JwtSvc, RtSvc, TokenOpts,
            NullLogger<RefreshTokenHandler>.Instance);

        var reuseResult = await reuseHandler.Handle(
            new RefreshTokenCommand(rawToken, null), CancellationToken.None);

        reuseResult.IsFailure.Should().BeTrue();
        reuseResult.Error.Code.Should().Be("AUTH_INVALID_REFRESH_TOKEN");

        // All tokens for user should now be revoked
        var user = await ctx3.Users.FirstAsync(u => u.NormalizedEmail == email.ToUpperInvariant());
        var anyActive = await ctx3.RefreshTokens
            .Where(t => t.UserId == user.Id && t.RevokedAt == null)
            .AnyAsync();
        anyActive.Should().BeFalse("all tokens should be revoked after reuse detection");
    }

    [SkippableFact]
    public async Task Duplicate_email_registration_returns_conflict()
    {
        await using var ctx = Db.CreateDbContext();
        var email = $"dup_{Guid.NewGuid():N}@example.com";

        var handler = new RegisterCustomerHandler(
            ctx, PwSvc, JwtSvc, RtSvc, TokenOpts,
            NullLogger<RegisterCustomerHandler>.Instance);

        await handler.Handle(
            new RegisterCustomerCommand(email, "Password1!", "A", "B", null, null),
            CancellationToken.None);

        await using var ctx2 = Db.CreateDbContext();
        var handler2 = new RegisterCustomerHandler(
            ctx2, PwSvc, JwtSvc, RtSvc, TokenOpts,
            NullLogger<RegisterCustomerHandler>.Instance);

        var result = await handler2.Handle(
            new RegisterCustomerCommand(email, "Password2!", "C", "D", null, null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AUTH_EMAIL_TAKEN");
    }
}
