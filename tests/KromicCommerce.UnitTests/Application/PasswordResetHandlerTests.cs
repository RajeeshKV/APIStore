using KromicCommerce.Application.Abstractions.Email;
using KromicCommerce.Application.Features.Auth.PasswordReset;
using Microsoft.Extensions.Logging.Abstractions;

namespace KromicCommerce.UnitTests.Application;

/// <summary>
/// Tests for RequestPasswordResetHandler and ResetPasswordHandler.
/// </summary>
public sealed class PasswordResetHandlerTests
{
    private readonly Mock<IApplicationDbContext> _db = new();
    private readonly Mock<IPasswordService> _pwdService = new();
    private readonly Mock<IEmailService> _emailService = new();

    // -----------------------------------------------------------------------
    // RequestPasswordResetHandler
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RequestReset_returns_success_for_unknown_email_without_leaking()
    {
        // No user found — must still return Success (prevents enumeration)
        SetupNoUser();
        var handler = BuildRequestHandler();

        var result = await handler.Handle(
            new RequestPasswordResetCommand("unknown@example.com"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _emailService.Verify(
            e => e.SendPasswordResetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RequestReset_returns_success_for_inactive_admin_without_leaking()
    {
        var user = User.CreateAdmin("admin@test.com", "hash", "Alice", "Admin");
        user.Deactivate();
        SetupUserByEmail("admin@test.com", user);
        var handler = BuildRequestHandler();

        var result = await handler.Handle(
            new RequestPasswordResetCommand("admin@test.com"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _emailService.Verify(
            e => e.SendPasswordResetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RequestReset_issues_token_and_sends_email_for_valid_admin()
    {
        var user = User.CreateAdmin("admin@test.com", "hash", "Alice", "Admin");
        SetupUserByEmail("admin@test.com", user);

        _pwdService.Setup(p => p.GenerateResetToken()).Returns("raw-token");
        _pwdService.Setup(p => p.HashToken("raw-token")).Returns("hashed-token");
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _emailService
            .Setup(e => e.SendPasswordResetAsync(
                It.IsAny<string>(), It.IsAny<string>(), "raw-token",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = BuildRequestHandler();
        var result = await handler.Handle(
            new RequestPasswordResetCommand("admin@test.com"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.PasswordResetTokenHash.Should().Be("hashed-token");
        user.PasswordResetTokenExpiresAt.Should().NotBeNull();
        _emailService.Verify(
            e => e.SendPasswordResetAsync("admin@test.com", It.IsAny<string>(), "raw-token", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // -----------------------------------------------------------------------
    // ResetPasswordHandler
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ResetPassword_returns_invalid_token_for_unknown_email()
    {
        SetupNoUser();
        var handler = BuildResetHandler();

        var result = await handler.Handle(
            new ResetPasswordCommand("ghost@test.com", "any-token", "NewPass1!"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RESET_TOKEN_INVALID");
    }

    [Fact]
    public async Task ResetPassword_returns_invalid_when_no_token_set()
    {
        var user = User.CreateAdmin("admin@test.com", "hash", "Alice", "Admin");
        // No SetPasswordResetToken called — PasswordResetTokenHash is null
        SetupUserByEmail("admin@test.com", user);
        var handler = BuildResetHandler();

        var result = await handler.Handle(
            new ResetPasswordCommand("admin@test.com", "any-token", "NewPass1!"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RESET_TOKEN_INVALID");
    }

    [Fact]
    public async Task ResetPassword_returns_invalid_for_expired_token()
    {
        var user = User.CreateAdmin("admin@test.com", "hash", "Alice", "Admin");
        user.SetPasswordResetToken("hashed-token", DateTime.UtcNow.AddMinutes(-1)); // expired
        SetupUserByEmail("admin@test.com", user);
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var handler = BuildResetHandler();

        var result = await handler.Handle(
            new ResetPasswordCommand("admin@test.com", "raw-token", "NewPass1!"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RESET_TOKEN_INVALID");
        user.PasswordResetTokenHash.Should().BeNull(); // expired token was cleared
    }

    [Fact]
    public async Task ResetPassword_returns_invalid_for_wrong_token()
    {
        var user = User.CreateAdmin("admin@test.com", "hash", "Alice", "Admin");
        user.SetPasswordResetToken("correct-hash", DateTime.UtcNow.AddMinutes(10));
        SetupUserByEmail("admin@test.com", user);
        // HashToken of "wrong-token" returns "wrong-hash" (different from "correct-hash")
        _pwdService.Setup(p => p.HashToken("wrong-token")).Returns("wrong-hash");
        var handler = BuildResetHandler();

        var result = await handler.Handle(
            new ResetPasswordCommand("admin@test.com", "wrong-token", "NewPass1!"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RESET_TOKEN_INVALID");
    }

    [Fact]
    public async Task ResetPassword_succeeds_and_invalidates_all_sessions()
    {
        var user = User.CreateAdmin("admin@test.com", "old-hash", "Alice", "Admin");
        var initialVersion = user.TokenVersion;
        user.SetPasswordResetToken("correct-hash", DateTime.UtcNow.AddMinutes(10));

        var rt1 = RefreshToken.Create(user.Id, "t1", DateTime.UtcNow.AddDays(7));
        var rt2 = RefreshToken.Create(user.Id, "t2", DateTime.UtcNow.AddDays(7));

        SetupUserByEmail("admin@test.com", user);
        SetupRefreshTokens(user.Id, [rt1, rt2]);

        _pwdService.Setup(p => p.HashToken("raw-token")).Returns("correct-hash");
        _pwdService.Setup(p => p.Hash("NewPass1!")).Returns("new-hash");
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = BuildResetHandler();
        var result = await handler.Handle(
            new ResetPasswordCommand("admin@test.com", "raw-token", "NewPass1!"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.PasswordHash.Should().Be("new-hash");
        user.TokenVersion.Should().Be(initialVersion + 1, "TokenVersion must be incremented to invalidate JWTs");
        user.PasswordResetTokenHash.Should().BeNull("reset token must be cleared after use");
        rt1.IsRevoked.Should().BeTrue("all refresh tokens must be revoked");
        rt2.IsRevoked.Should().BeTrue("all refresh tokens must be revoked");
    }

    [Fact]
    public async Task ResetPassword_token_is_single_use()
    {
        // After a successful reset, the token hash is cleared — a second attempt must fail
        var user = User.CreateAdmin("admin@test.com", "old-hash", "Alice", "Admin");
        user.SetPasswordResetToken("correct-hash", DateTime.UtcNow.AddMinutes(10));
        SetupUserByEmail("admin@test.com", user);
        SetupRefreshTokens(user.Id, []);

        _pwdService.Setup(p => p.HashToken("raw-token")).Returns("correct-hash");
        _pwdService.Setup(p => p.Hash(It.IsAny<string>())).Returns("new-hash");
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = BuildResetHandler();

        // First use succeeds
        var first = await handler.Handle(
            new ResetPasswordCommand("admin@test.com", "raw-token", "NewPass1!"),
            CancellationToken.None);
        first.IsSuccess.Should().BeTrue();

        // Token is now cleared — second use must fail
        var second = await handler.Handle(
            new ResetPasswordCommand("admin@test.com", "raw-token", "AnotherPass2!"),
            CancellationToken.None);
        second.IsFailure.Should().BeTrue();
        second.Error.Code.Should().Be("RESET_TOKEN_INVALID");
    }

    // -----------------------------------------------------------------------
    // Validator — RequestPasswordReset
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void RequestReset_validator_rejects_invalid_email(string email)
    {
        var validator = new RequestPasswordResetValidator();
        var result = validator.Validate(new RequestPasswordResetCommand(email));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void RequestReset_validator_accepts_valid_email()
    {
        var validator = new RequestPasswordResetValidator();
        validator.Validate(new RequestPasswordResetCommand("admin@test.com")).IsValid.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void SetupNoUser()
    {
        _db.Setup(d => d.Users).Returns(CreateMockDbSet<User>([]));
    }

    private void SetupUserByEmail(string email, User user)
    {
        _db.Setup(d => d.Users).Returns(CreateMockDbSet([user]));
        _db.Setup(d => d.RefreshTokens)
           .Returns(CreateMockDbSet<RefreshToken>([]));
    }

    private void SetupRefreshTokens(Guid userId, List<RefreshToken> tokens)
    {
        _db.Setup(d => d.RefreshTokens).Returns(CreateMockDbSet(tokens));
    }

    private RequestPasswordResetHandler BuildRequestHandler() =>
        new(_db.Object, _pwdService.Object, _emailService.Object,
            NullLogger<RequestPasswordResetHandler>.Instance);

    private ResetPasswordHandler BuildResetHandler() =>
        new(_db.Object, _pwdService.Object,
            NullLogger<ResetPasswordHandler>.Instance);

    private static Microsoft.EntityFrameworkCore.DbSet<T> CreateMockDbSet<T>(
        List<T> data) where T : class
    {
        var queryable = data.AsQueryable();
        var mockSet = new Mock<Microsoft.EntityFrameworkCore.DbSet<T>>();
        mockSet.As<IQueryable<T>>().Setup(m => m.Provider)
            .Returns(new TestAsyncQueryProvider<T>(queryable.Provider));
        mockSet.As<IQueryable<T>>().Setup(m => m.Expression).Returns(queryable.Expression);
        mockSet.As<IQueryable<T>>().Setup(m => m.ElementType).Returns(queryable.ElementType);
        mockSet.As<IQueryable<T>>().Setup(m => m.GetEnumerator()).Returns(queryable.GetEnumerator());
        mockSet.As<IAsyncEnumerable<T>>()
            .Setup(m => m.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns(new TestAsyncEnumerator<T>(queryable.GetEnumerator()));
        return mockSet.Object;
    }
}
