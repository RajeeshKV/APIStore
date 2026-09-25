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
        Microsoft.Extensions.Options.Options.Create(new AuthTokenOptions { AccessTokenExpiryMinutes = 15, RefreshTokenExpiryDays = 30 });

    private LoginWithEmailHandler CreateHandler() =>
        new(_db.Object, _pw.Object, _jwt.Object, _rts.Object, _opts,
            NullLogger<LoginWithEmailHandler>.Instance);

    [Fact]
    public async Task Returns_failure_when_user_not_found()
    {
        SetupDbNoUser();
        var result = await CreateHandler().Handle(
            new LoginWithEmailCommand("x@x.com", "pass", null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AUTH_INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task Returns_failure_when_password_wrong()
    {
        var user = User.CreateCustomer("test@example.com", "hash", "A", "B");
        SetupDbWithUser(user);
        _pw.Setup(p => p.Verify("wrong", "hash")).Returns(false);

        var result = await CreateHandler().Handle(
            new LoginWithEmailCommand("test@example.com", "wrong", null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AUTH_INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task Returns_token_on_success()
    {
        var user = User.CreateCustomer("test@example.com", "hash", "A", "B");
        SetupDbWithUser(user);
        _pw.Setup(p => p.Verify("correct", "hash")).Returns(true);
        _rts.Setup(r => r.GenerateRawToken()).Returns("raw");
        _rts.Setup(r => r.HashToken("raw")).Returns("hashed");
        _jwt.Setup(j => j.IssueAccessToken(user.Id, user.Email, "Customer", 1))
            .Returns("access_token");

        var mockSet = new Mock<DbSet<RefreshToken>>();
        _db.Setup(d => d.RefreshTokens).Returns(mockSet.Object);
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateHandler().Handle(
            new LoginWithEmailCommand("test@example.com", "correct", null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().Be("access_token");
        result.Value.RefreshToken.Should().Be("raw");
    }

    [Fact]
    public async Task Returns_failure_for_inactive_user()
    {
        var user = User.CreateCustomer("test@example.com", "hash", "A", "B");
        user.Deactivate();
        SetupDbWithUser(user);

        var result = await CreateHandler().Handle(
            new LoginWithEmailCommand("test@example.com", "pass", null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AUTH_INVALID_CREDENTIALS");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void SetupDbNoUser()
    {
        var users = new List<User>().AsQueryable();
        var mockSet = MockDbSet(users);
        _db.Setup(d => d.Users).Returns(mockSet.Object);
    }

    private void SetupDbWithUser(User user)
    {
        var users = new List<User> { user }.AsQueryable();
        var mockSet = MockDbSet(users);
        _db.Setup(d => d.Users).Returns(mockSet.Object);
    }

    private static Mock<DbSet<T>> MockDbSet<T>(IQueryable<T> data) where T : class
    {
        var mock = new Mock<DbSet<T>>();
        mock.As<IAsyncEnumerable<T>>()
            .Setup(m => m.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns(new TestAsyncEnumerator<T>(data.GetEnumerator()));
        mock.As<IQueryable<T>>().Setup(m => m.Provider)
            .Returns(new TestAsyncQueryProvider<T>(data.Provider));
        mock.As<IQueryable<T>>().Setup(m => m.Expression).Returns(data.Expression);
        mock.As<IQueryable<T>>().Setup(m => m.ElementType).Returns(data.ElementType);
        mock.As<IQueryable<T>>().Setup(m => m.GetEnumerator()).Returns(data.GetEnumerator());
        return mock;
    }
}
