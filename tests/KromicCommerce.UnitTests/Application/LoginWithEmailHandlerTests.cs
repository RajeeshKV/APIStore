using KromicCommerce.Application.Features.Auth.LoginWithEmail;
using KromicCommerce.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RefreshToken = KromicCommerce.Domain.Identity.RefreshToken;

namespace KromicCommerce.UnitTests.Application;

public sealed class LoginWithEmailHandlerTests
{
    private readonly Mock<IApplicationDbContext> _db = new();
    private readonly Mock<IPasswordService> _pw = new();
    private readonly Mock<IJwtService> _jwt = new();
    private readonly Mock<IRefreshTokenService> _rts = new();
    private readonly IOptions<AuthTokenOptions> _opts =
        Microsoft.Extensions.Options.Options.Create(
            new AuthTokenOptions { AccessTokenExpiryMinutes = 15, RefreshTokenExpiryDays = 30 });

    private LoginWithEmailHandler CreateHandler() =>
        new(_db.Object, _pw.Object, _jwt.Object, _rts.Object, _opts,
            NullLogger<LoginWithEmailHandler>.Instance);

    // -------------------------------------------------------------------------
    // User-not-found / inactive
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Returns_failure_when_user_not_found()
    {
        SetupDbNoUser();

        var result = await CreateHandler().Handle(
            new LoginWithEmailCommand("nobody@example.com", "pass", null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AUTH_INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task Returns_failure_for_inactive_user()
    {
        var user = User.CreateCustomer("test@example.com", "hash", "A", "B");
        user.Deactivate();
        SetupDbWithUser(user);

        var result = await CreateHandler().Handle(
            new LoginWithEmailCommand("test@example.com", "pass", null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AUTH_INVALID_CREDENTIALS");
    }

    // -------------------------------------------------------------------------
    // Password
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Returns_failure_when_password_wrong()
    {
        var user = User.CreateCustomer("test@example.com", "hash", "A", "B");
        SetupDbWithUser(user);
        _pw.Setup(p => p.Verify("wrong", "hash")).Returns(false);

        var result = await CreateHandler().Handle(
            new LoginWithEmailCommand("test@example.com", "wrong", null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AUTH_INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task Returns_failure_for_oauth_only_account_no_password_hash()
    {
        // Google-only account has null PasswordHash
        var user = User.CreateCustomer("google@example.com", passwordHash: null, "G", "User");
        SetupDbWithUser(user);

        var result = await CreateHandler().Handle(
            new LoginWithEmailCommand("google@example.com", "any-password", null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AUTH_INVALID_CREDENTIALS");
    }

    // -------------------------------------------------------------------------
    // Login by email
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Returns_token_on_login_with_email()
    {
        var user = User.CreateAdmin("admin@example.com", "hash", "Admin", "User");
        SetupDbWithUser(user);
        _pw.Setup(p => p.Verify("correct", "hash")).Returns(true);
        SetupTokenIssuance(user);

        var result = await CreateHandler().Handle(
            new LoginWithEmailCommand("admin@example.com", "correct", null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().Be("access_token");
        result.Value.RefreshToken.Should().Be("raw");
    }

    // -------------------------------------------------------------------------
    // Login by username
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Returns_token_on_login_with_username()
    {
        var user = User.CreateAdmin("admin@example.com", "hash", "Admin", "User");
        user.SetUsername("kromic_admin");
        SetupDbWithUser(user);
        _pw.Setup(p => p.Verify("correct", "hash")).Returns(true);
        SetupTokenIssuance(user);

        // Submit the username, not the email
        var result = await CreateHandler().Handle(
            new LoginWithEmailCommand("kromic_admin", "correct", null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().Be("access_token");
    }

    [Fact]
    public async Task Username_lookup_is_case_insensitive()
    {
        var user = User.CreateAdmin("admin@example.com", "hash", "Admin", "User");
        user.SetUsername("KromicAdmin");
        SetupDbWithUser(user);
        _pw.Setup(p => p.Verify("correct", "hash")).Returns(true);
        SetupTokenIssuance(user);

        var result = await CreateHandler().Handle(
            new LoginWithEmailCommand("KROMICADMIN", "correct", null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // Validator
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("", "pass")]           // empty identifier
    [InlineData("admin", "")]          // empty password
    public void Validator_rejects_missing_fields(string identifier, string password)
    {
        var validator = new LoginWithEmailValidator();
        var result = validator.Validate(new LoginWithEmailCommand(identifier, password, null));
        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("admin@example.com", "password")]   // email
    [InlineData("kromic_admin", "password")]         // username
    public void Validator_accepts_email_or_username(string identifier, string password)
    {
        var validator = new LoginWithEmailValidator();
        var result = validator.Validate(new LoginWithEmailCommand(identifier, password, null));
        result.IsValid.Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void SetupDbNoUser()
    {
        _db.Setup(d => d.Users).Returns(MockDbSet(new List<User>()).Object);
    }

    private void SetupDbWithUser(User user)
    {
        _db.Setup(d => d.Users).Returns(MockDbSet(new List<User> { user }).Object);
    }

    private void SetupTokenIssuance(User user)
    {
        _rts.Setup(r => r.GenerateRawToken()).Returns("raw");
        _rts.Setup(r => r.HashToken("raw")).Returns("hashed");
        _jwt.Setup(j => j.IssueAccessToken(user.Id, user.Email, It.IsAny<string>(), It.IsAny<int>()))
            .Returns("access_token");

        var mockSet = new Mock<DbSet<RefreshToken>>();
        _db.Setup(d => d.RefreshTokens).Returns(mockSet.Object);
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private static Mock<DbSet<T>> MockDbSet<T>(List<T> data) where T : class
    {
        var queryable = data.AsQueryable();
        var mock = new Mock<DbSet<T>>();
        mock.As<IAsyncEnumerable<T>>()
            .Setup(m => m.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns(new TestAsyncEnumerator<T>(queryable.GetEnumerator()));
        mock.As<IQueryable<T>>()
            .Setup(m => m.Provider)
            .Returns(new TestAsyncQueryProvider<T>(queryable.Provider));
        mock.As<IQueryable<T>>().Setup(m => m.Expression).Returns(queryable.Expression);
        mock.As<IQueryable<T>>().Setup(m => m.ElementType).Returns(queryable.ElementType);
        mock.As<IQueryable<T>>().Setup(m => m.GetEnumerator()).Returns(queryable.GetEnumerator());
        return mock;
    }
}
